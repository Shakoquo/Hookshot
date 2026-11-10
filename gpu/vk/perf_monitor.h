// Built-in Mako performance monitor.
// Kept as a small module so later Mako versions only need the menu/draw, frame, and GPU-time hooks.
#pragma once

#include <cstdint>
#include "vk_paths.h"

namespace mkwvk {

// Lightweight hooks. These may stay active while the overlay is hidden.
void perf_monitor_note_frame(bool game_frame);
void perf_monitor_note_gpu_time(uint64_t ns);

// Samples process/GPU memory and rolling frame statistics. True when the displayed values changed.
bool perf_monitor_sample();

// The rows shown (labels and values, 6 of them; values 40 bytes each): drawn by vk_menu.cpp draw_perf
int perf_monitor_rows(const char* labels[], char values[][40]);

} // namespace mkwvk
