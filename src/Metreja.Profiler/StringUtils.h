#pragma once

#include <string>

inline size_t FindLastPathSeparator(const std::string& path) { return path.find_last_of("/\\"); }
