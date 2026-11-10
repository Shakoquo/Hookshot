// Built-in Mako performance monitor: process CPU/RAM, Vulkan VRAM, GPU time, frame time and game FPS.
// The stats/drawing live here so porting to another Mako tree only needs a few small integration hooks.
#include "perf_monitor.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>

#include <psapi.h>   // after vk_paths.h/windows.h: MinGW's psapi.h needs the Win32 types first

#ifndef _WIN32
#include <time.h>
#include <unistd.h>
#endif

namespace mkwvk {

// vk_vma.cpp: current process use of device-local heaps and the driver's current budget.
bool vram_usage(uint64_t* used, uint64_t* budget, uint64_t* block_free);

namespace {

constexpr int64_t kSampleNs = 500000000LL;
constexpr int64_t kResetNs = 2000000000LL;
constexpr int kRows = 6;


int64_t now_ns() {
    return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

std::atomic<uint64_t> g_game_frames{0};
std::atomic<uint64_t> g_gpu_ns{0};

struct Snapshot {
    double cpu_pct = 0.0;
    double frame_ms = 0.0;
    double gpu_ms = 0.0;
    double fps = 0.0;
    uint64_t ram_used = 0, ram_total = 0;
    uint64_t vram_used = 0, vram_budget = 0;
    bool have_rate = false;
    bool have_cpu = false;
    bool have_gpu = false;
} snap;

uint64_t process_cpu_ns() {
#ifdef _WIN32
    FILETIME create{}, exit{}, kernel{}, user{};
    if (!GetProcessTimes(GetCurrentProcess(), &create, &exit, &kernel, &user)) return 0;
    auto ticks = [](const FILETIME& f) -> uint64_t {
        return ((uint64_t)f.dwHighDateTime << 32) | f.dwLowDateTime;
    };
    return (ticks(kernel) + ticks(user)) * 100ull;   // FILETIME is in 100 ns units
#else
    timespec ts{};
    if (clock_gettime(CLOCK_PROCESS_CPUTIME_ID, &ts) != 0) return 0;
    return (uint64_t)ts.tv_sec * 1000000000ull + (uint64_t)ts.tv_nsec;
#endif
}

uint32_t logical_cpus() {
    SYSTEM_INFO si{};
    GetSystemInfo(&si);
    return std::max<DWORD>(si.dwNumberOfProcessors, 1);
}

uint64_t total_physical_ram() {
#ifdef _WIN32
    MEMORYSTATUSEX ms{};
    ms.dwLength = sizeof ms;
    return GlobalMemoryStatusEx(&ms) ? (uint64_t)ms.ullTotalPhys : 0;
#else
    const long pages = sysconf(_SC_PHYS_PAGES), page = sysconf(_SC_PAGESIZE);
    return pages > 0 && page > 0 ? (uint64_t)pages * (uint64_t)page : 0;
#endif
}

uint64_t process_working_set() {
    PROCESS_MEMORY_COUNTERS_EX pm{};
    pm.cb = sizeof pm;
#ifdef _WIN32
    // Resolve this at runtime so hookshot_vk.dll does not gain a psapi.lib link dependency.
    using Fn = BOOL (WINAPI *)(HANDLE, PROCESS_MEMORY_COUNTERS*, DWORD);
    static Fn fn = [] {
        HMODULE k32 = GetModuleHandleA("kernel32.dll");
        Fn p = k32 ? (Fn)(void*)GetProcAddress(k32, "K32GetProcessMemoryInfo") : nullptr;
        if (p) return p;
        HMODULE psapi = LoadLibraryA("psapi.dll");
        return psapi ? (Fn)(void*)GetProcAddress(psapi, "GetProcessMemoryInfo") : nullptr;
    }();
    return fn && fn(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&pm, sizeof pm) ? (uint64_t)pm.WorkingSetSize : 0;
#else
    return GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&pm, sizeof pm) ? (uint64_t)pm.WorkingSetSize : 0;
#endif
}

void format_bytes(char* out, size_t n, uint64_t used, uint64_t total) {
    constexpr double GiB = 1024.0 * 1024.0 * 1024.0;
    if (!total) snprintf(out, n, "%.1f GB", used / GiB);
    else snprintf(out, n, "%.1f / %.1f GB", used / GiB, total / GiB);
}

} // namespace

void perf_monitor_note_frame(bool game_frame) {
    if (game_frame) g_game_frames.fetch_add(1, std::memory_order_relaxed);
}

void perf_monitor_note_gpu_time(uint64_t ns) {
    g_gpu_ns.fetch_add(ns, std::memory_order_relaxed);
}

bool perf_monitor_sample() {
    static int64_t last_at = 0;
    static uint64_t last_frames = 0, last_gpu = 0, last_cpu = 0;

    const int64_t now = now_ns();
    const uint64_t frames = g_game_frames.load(std::memory_order_relaxed);
    const uint64_t gpu = g_gpu_ns.load(std::memory_order_relaxed);
    const uint64_t cpu = process_cpu_ns();

    const bool reset = !last_at || now - last_at > kResetNs;
    if (!reset && now - last_at < kSampleNs) return false;

    // RAM/VRAM are useful immediately, even on the first sample before a rate interval exists.
    snap.ram_used = process_working_set();
    snap.ram_total = total_physical_ram();
    if (!vram_usage(&snap.vram_used, &snap.vram_budget, nullptr)) snap.vram_used = snap.vram_budget = 0;

    if (!reset) {
        const int64_t elapsed = now - last_at;
        const uint64_t df = frames - last_frames;
        const uint64_t dgpu = gpu - last_gpu;
        if (elapsed > 0 && df) {
            const double secs = elapsed / 1e9;
            snap.fps = df / secs;
            snap.frame_ms = secs * 1000.0 / df;
            // Sum all Vulkan recording work completed during the interval, then express it per game frame.
            snap.gpu_ms = dgpu / 1e6 / (double)df;
            snap.have_rate = true;
            snap.have_gpu = dgpu != 0;
        } else {
            snap.have_rate = false;
            snap.have_gpu = false;
        }
        if (elapsed > 0 && cpu >= last_cpu && last_cpu) {
            snap.cpu_pct = (cpu - last_cpu) / (double)elapsed * 100.0 / logical_cpus();
            snap.cpu_pct = std::clamp(snap.cpu_pct, 0.0, 100.0);
            snap.have_cpu = true;
        } else snap.have_cpu = false;
    } else {
        snap.have_rate = snap.have_cpu = snap.have_gpu = false;
    }

    last_at = now;
    last_frames = frames;
    last_gpu = gpu;
    last_cpu = cpu;
    return true;
}

int perf_monitor_rows(const char* labels[], char values[][40]) {
    static const char* names[kRows] = {"CPU usage", "Frame time", "Graphics VRAM", "GPU time", "RAM", "Framerate"};
    for (int i = 0; i < kRows; i++) labels[i] = names[i];
    if (snap.have_cpu) snprintf(values[0], 40, "%.1f%%", snap.cpu_pct);
    else strcpy(values[0], "--");
    if (snap.have_rate) snprintf(values[1], 40, "%.2f ms", snap.frame_ms);
    else strcpy(values[1], "--");
    format_bytes(values[2], 40, snap.vram_used, snap.vram_budget);
    if (snap.have_gpu) snprintf(values[3], 40, "%.2f ms", snap.gpu_ms);
    else strcpy(values[3], "--");
    format_bytes(values[4], 40, snap.ram_used, snap.ram_total);
    if (snap.have_rate) snprintf(values[5], 40, "%.1f FPS", snap.fps);
    else strcpy(values[5], "--");
    return kRows;
}

} // namespace mkwvk
