#pragma once

// Payload archive used by Packer (build time) and Setup (run time).
//
// Layout (little endian):
//   char[8]  magic "UPSETUP1"
//   uint32   entry count
//   entries:
//     uint32   path length in UTF-16 code units
//     wchar_t  relative path (backslashes, no terminator)
//     uint32   algorithm (0 = stored, otherwise COMPRESS_ALGORITHM_*)
//     uint64   original size
//     uint64   stored size
//     byte[]   stored data

#include <cstdint>

namespace archive
{
    constexpr char Magic[8] = { 'U', 'P', 'S', 'E', 'T', 'U', 'P', '1' };
    constexpr uint32_t Stored = 0;

    // files smaller than this are stored as they are
    constexpr uint64_t MinCompressSize = 4096;
}
