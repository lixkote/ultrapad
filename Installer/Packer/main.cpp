// Packer: builds the payload archive embedded in UltraPad-Setup.exe.
//
// Usage: Packer.exe <output.bin> <folder>[=<prefix>] [<folder>[=<prefix>] ...]
// Every file under each folder is added; <prefix> is prepended to its path inside the archive.

#include <windows.h>
#include <compressapi.h>

#include <cstdio>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

#include "../Common/Archive.h"

namespace fs = std::filesystem;

namespace
{
    struct Input
    {
        fs::path folder;
        std::wstring prefix;
    };

    std::vector<uint8_t> ReadFile(fs::path const& path)
    {
        std::ifstream in(path, std::ios::binary);
        return std::vector<uint8_t>((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
    }

    bool CompressData(DWORD algorithm, std::vector<uint8_t> const& data, std::vector<uint8_t>& out)
    {
        COMPRESSOR_HANDLE compressor = nullptr;
        if (!CreateCompressor(algorithm, nullptr, &compressor)) return false;

        SIZE_T needed = 0;
        ::Compress(compressor, const_cast<uint8_t*>(data.data()), data.size(), nullptr, 0, &needed);
        out.resize(needed);
        SIZE_T written = 0;
        BOOL ok = ::Compress(compressor, const_cast<uint8_t*>(data.data()), data.size(), out.data(), out.size(), &written);
        CloseCompressor(compressor);
        if (!ok) return false;
        out.resize(written);
        return true;
    }

    template <typename T>
    void Write(std::ofstream& out, T const& value)
    {
        out.write(reinterpret_cast<char const*>(&value), sizeof(T));
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3)
    {
        fwprintf(stderr, L"Usage: Packer.exe <output.bin> <folder>[=<prefix>] ...\n");
        return 1;
    }

    std::vector<Input> inputs;
    for (int i = 2; i < argc; ++i)
    {
        std::wstring arg = argv[i];
        Input input;
        size_t eq = arg.find(L'=');
        if (eq != std::wstring::npos)
        {
            input.folder = arg.substr(0, eq);
            input.prefix = arg.substr(eq + 1);
            if (!input.prefix.empty() && input.prefix.back() != L'\\') input.prefix += L'\\';
        }
        else
        {
            input.folder = arg;
        }

        if (!fs::is_directory(input.folder))
        {
            fwprintf(stderr, L"Packer: folder not found: %s\n", input.folder.c_str());
            return 2;
        }
        inputs.push_back(input);
    }

    struct Entry
    {
        fs::path source;
        std::wstring name;
    };
    std::vector<Entry> entries;
    for (Input const& input : inputs)
    {
        for (auto const& item : fs::recursive_directory_iterator(input.folder))
        {
            if (!item.is_regular_file()) continue;
            std::wstring relative = fs::relative(item.path(), input.folder).wstring();
            // build leftovers that are never needed at run time
            fs::path ext = item.path().extension();
            if (ext == L".pdb" || ext == L".lib" || ext == L".exp" || ext == L".ilk" || ext == L".md" || ext == L".txt") continue;
            entries.push_back({ item.path(), input.prefix + relative });
        }
    }

    fs::path outputPath = argv[1];
    fs::create_directories(outputPath.parent_path());
    std::ofstream out(outputPath, std::ios::binary | std::ios::trunc);
    if (!out)
    {
        fwprintf(stderr, L"Packer: cannot write %s\n", outputPath.c_str());
        return 3;
    }

    out.write(archive::Magic, sizeof(archive::Magic));
    Write(out, static_cast<uint32_t>(entries.size()));

    uint64_t totalIn = 0, totalOut = 0;
    for (Entry const& entry : entries)
    {
        std::vector<uint8_t> data = ReadFile(entry.source);
        std::vector<uint8_t> packed;
        uint32_t algorithm = archive::Stored;

        if (data.size() >= archive::MinCompressSize)
        {
            if (CompressData(COMPRESS_ALGORITHM_LZMS, data, packed) && packed.size() < data.size())
                algorithm = COMPRESS_ALGORITHM_LZMS;
        }
        std::vector<uint8_t> const& stored = algorithm == archive::Stored ? data : packed;

        Write(out, static_cast<uint32_t>(entry.name.size()));
        out.write(reinterpret_cast<char const*>(entry.name.data()), entry.name.size() * sizeof(wchar_t));
        Write(out, algorithm);
        Write(out, static_cast<uint64_t>(data.size()));
        Write(out, static_cast<uint64_t>(stored.size()));
        out.write(reinterpret_cast<char const*>(stored.data()), stored.size());

        totalIn += data.size();
        totalOut += stored.size();
    }

    if (!out)
    {
        fwprintf(stderr, L"Packer: write failed\n");
        return 4;
    }

    wprintf(L"Packer: %zu files, %.1f MB -> %.1f MB\n", entries.size(), totalIn / 1048576.0, totalOut / 1048576.0);
    return 0;
}
