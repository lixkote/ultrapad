// UltraPad-Setup.exe: the single file that users download.
//
// It is elevated once by its manifest (the only UAC prompt), unpacks the installer UI and the
// UltraPad package from its PAYLOAD resource into a temporary folder, runs the installer UI with the
// same command line (it inherits the elevation, so there is no second prompt), waits for it and
// removes the temporary folder again.

#include <windows.h>
#include <compressapi.h>
#include <shellapi.h>

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

#include "../Common/Archive.h"
#include "resource.h"

namespace
{
    std::wstring g_tempRoot;

    void ShowError(wchar_t const* text)
    {
        bool polish = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_POLISH;
        MessageBoxW(nullptr, text, polish ? L"Instalator UltraPad" : L"UltraPad Setup", MB_ICONERROR | MB_OK);
    }

    bool CreateFolders(std::wstring const& path)
    {
        size_t pos = g_tempRoot.size();
        while ((pos = path.find(L'\\', pos + 1)) != std::wstring::npos)
        {
            std::wstring part = path.substr(0, pos);
            if (!CreateDirectoryW(part.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) return false;
        }
        return true;
    }

    bool WriteAll(std::wstring const& path, void const* data, size_t size)
    {
        if (!CreateFolders(path)) return false;
        HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;

        auto bytes = static_cast<uint8_t const*>(data);
        bool ok = true;
        while (size > 0 && ok)
        {
            DWORD chunk = size > 0x10000000 ? 0x10000000 : static_cast<DWORD>(size);
            DWORD written = 0;
            ok = WriteFile(file, bytes, chunk, &written, nullptr) && written == chunk;
            bytes += chunk;
            size -= chunk;
        }
        CloseHandle(file);
        return ok;
    }

    class Reader
    {
    public:
        Reader(uint8_t const* data, size_t size) : m_data(data), m_size(size) {}

        bool Read(void* out, size_t size)
        {
            if (m_size - m_pos < size) return false;
            memcpy(out, m_data + m_pos, size);
            m_pos += size;
            return true;
        }

        uint8_t const* Take(size_t size)
        {
            if (m_size - m_pos < size) return nullptr;
            uint8_t const* p = m_data + m_pos;
            m_pos += size;
            return p;
        }

    private:
        uint8_t const* m_data;
        size_t m_size;
        size_t m_pos = 0;
    };

    bool Extract(uint8_t const* data, size_t size)
    {
        Reader reader(data, size);
        char magic[8];
        uint32_t count = 0;
        if (!reader.Read(magic, sizeof(magic)) || memcmp(magic, archive::Magic, sizeof(magic)) != 0) return false;
        if (!reader.Read(&count, sizeof(count))) return false;

        std::vector<uint8_t> buffer;
        for (uint32_t i = 0; i < count; ++i)
        {
            uint32_t nameLength = 0, algorithm = 0;
            uint64_t originalSize = 0, storedSize = 0;
            if (!reader.Read(&nameLength, sizeof(nameLength)) || nameLength == 0 || nameLength > 1024) return false;

            std::wstring name(nameLength, L'\0');
            if (!reader.Read(name.data(), nameLength * sizeof(wchar_t))) return false;
            if (!reader.Read(&algorithm, sizeof(algorithm)) ||
                !reader.Read(&originalSize, sizeof(originalSize)) ||
                !reader.Read(&storedSize, sizeof(storedSize))) return false;

            // never write outside the temporary folder
            if (name.find(L"..") != std::wstring::npos || name.find(L':') != std::wstring::npos || name[0] == L'\\') return false;

            uint8_t const* stored = reader.Take(static_cast<size_t>(storedSize));
            if (!stored) return false;

            std::wstring target = g_tempRoot + L"\\" + name;
            if (algorithm == archive::Stored)
            {
                if (!WriteAll(target, stored, static_cast<size_t>(storedSize))) return false;
                continue;
            }

            DECOMPRESSOR_HANDLE decompressor = nullptr;
            if (!CreateDecompressor(algorithm, nullptr, &decompressor)) return false;
            buffer.resize(static_cast<size_t>(originalSize));
            SIZE_T produced = 0;
            BOOL ok = Decompress(decompressor, const_cast<uint8_t*>(stored), static_cast<SIZE_T>(storedSize),
                                 buffer.data(), buffer.size(), &produced);
            CloseDecompressor(decompressor);
            if (!ok || produced != originalSize) return false;
            if (!WriteAll(target, buffer.data(), buffer.size())) return false;
        }
        return true;
    }

    void DeleteTree(std::wstring const& folder)
    {
        WIN32_FIND_DATAW data;
        HANDLE find = FindFirstFileW((folder + L"\\*").c_str(), &data);
        if (find != INVALID_HANDLE_VALUE)
        {
            do
            {
                if (wcscmp(data.cFileName, L".") == 0 || wcscmp(data.cFileName, L"..") == 0) continue;
                std::wstring path = folder + L"\\" + data.cFileName;
                if (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
                {
                    DeleteTree(path);
                }
                else
                {
                    SetFileAttributesW(path.c_str(), FILE_ATTRIBUTE_NORMAL);
                    DeleteFileW(path.c_str());
                }
            } while (FindNextFileW(find, &data));
            FindClose(find);
        }
        RemoveDirectoryW(folder.c_str());
    }

    // everything after the program name, passed on to the installer UI unchanged
    std::wstring Arguments()
    {
        wchar_t const* line = GetCommandLineW();
        bool quoted = false;
        while (*line && (quoted || (*line != L' ' && *line != L'\t')))
        {
            if (*line == L'"') quoted = !quoted;
            ++line;
        }
        while (*line == L' ' || *line == L'\t') ++line;
        return line;
    }
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    bool polish = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_POLISH;

    HRSRC resource = FindResourceW(instance, MAKEINTRESOURCEW(IDR_PAYLOAD), RT_RCDATA);
    HGLOBAL loaded = resource ? LoadResource(instance, resource) : nullptr;
    auto data = loaded ? static_cast<uint8_t const*>(LockResource(loaded)) : nullptr;
    DWORD size = resource ? SizeofResource(instance, resource) : 0;
    if (!data || size == 0)
    {
        ShowError(polish ? L"Instalator jest uszkodzony. Pobierz go ponownie." : L"The installer is damaged. Please download it again.");
        return 1;
    }

    wchar_t temp[MAX_PATH];
    GetTempPathW(MAX_PATH, temp);
    g_tempRoot = std::wstring(temp) + L"UltraPadSetup-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64());
    if (!CreateDirectoryW(g_tempRoot.c_str(), nullptr))
    {
        ShowError(polish ? L"Nie można utworzyć folderu tymczasowego." : L"Could not create a temporary folder.");
        return 1;
    }

    HCURSOR previous = SetCursor(LoadCursorW(nullptr, IDC_APPSTARTING));
    bool extracted = Extract(data, size);
    SetCursor(previous);
    if (!extracted)
    {
        DeleteTree(g_tempRoot);
        ShowError(polish ? L"Nie można rozpakować instalatora. Sprawdź, czy na dysku jest wolne miejsce, i spróbuj ponownie."
                         : L"Could not unpack the installer. Make sure there is free disk space and try again.");
        return 1;
    }

    std::wstring exe = g_tempRoot + L"\\SetupUI.exe";
    std::wstring commandLine = L"\"" + exe + L"\" " + Arguments();

    STARTUPINFOW startup{ sizeof(startup) };
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(exe.c_str(), commandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, g_tempRoot.c_str(), &startup, &process))
    {
        DeleteTree(g_tempRoot);
        ShowError(polish ? L"Nie można uruchomić instalatora." : L"Could not start the installer.");
        return 1;
    }

    // the installer UI decides where the foreground goes
    AllowSetForegroundWindow(process.dwProcessId);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(process.hProcess, &exitCode);
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);

    // the UI's files can stay locked for a moment after it exits
    for (int attempt = 0; attempt < 10; ++attempt)
    {
        DeleteTree(g_tempRoot);
        if (GetFileAttributesW(g_tempRoot.c_str()) == INVALID_FILE_ATTRIBUTES) break;
        Sleep(300);
    }
    return static_cast<int>(exitCode);
}
