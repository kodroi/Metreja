#pragma once

#include <string>

inline size_t FindLastPathSeparator(const std::string& path)
{
    size_t pos = path.rfind('\\');
    if (pos == std::string::npos)
        pos = path.rfind('/');
    return pos;
}
