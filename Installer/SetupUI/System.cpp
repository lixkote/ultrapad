#include "pch.h"
#include "Strings.h"
#include "System.h"

#include <windows.h>
#include <AppxPackaging.h>
#include <exdisp.h>
#include <msi.h>
#include <shellapi.h>
#include <shldisp.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <softpub.h>
#include <wincrypt.h>
#include <wintrust.h>

#include <cstdarg>
#include <cstdio>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "msi.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "wintrust.lib")

namespace
{
    wchar_t const MarkerKey[] = L"SOFTWARE\\Lixkote\\UltraPad Setup";
    wchar_t const VendorKey[] = L"SOFTWARE\\Lixkote";
    wchar_t const LegacyUpgradeCode[] = L"{7B0AF2D8-D27B-4A07-9BE5-88185A9153BE}";

    template <typename T>
    class Com
    {
    public:
        Com() = default;
        Com(Com const&) = delete;
        Com& operator=(Com const&) = delete;
        ~Com() { if (m_ptr) m_ptr->Release(); }

        T* operator->() const { return m_ptr; }
        T* get() const { return m_ptr; }
        T** put() { if (m_ptr) { m_ptr->Release(); m_ptr = nullptr; } return &m_ptr; }
        void** put_void() { return reinterpret_cast<void**>(put()); }
        explicit operator bool() const { return m_ptr != nullptr; }

    private:
        T* m_ptr = nullptr;
    };

    std::wstring TakeString(LPWSTR value)
    {
        std::wstring result = value ? value : L"";
        CoTaskMemFree(value);
        return result;
    }

    bool EndsWith(std::wstring const& text, wchar_t const* suffix)
    {
        size_t length = wcslen(suffix);
        return text.size() >= length && _wcsicmp(text.c_str() + text.size() - length, suffix) == 0;
    }

    bool FolderExists(std::wstring const& path)
    {
        DWORD attributes = GetFileAttributesW(path.c_str());
        return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY);
    }

    std::vector<std::wstring> FindFiles(std::wstring const& folder, std::initializer_list<wchar_t const*> extensions)
    {
        std::vector<std::wstring> files;
        WIN32_FIND_DATAW data;
        HANDLE find = FindFirstFileW((folder + L"\\*").c_str(), &data);
        if (find == INVALID_HANDLE_VALUE) return files;
        do
        {
            if (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;
            std::wstring name = data.cFileName;
            for (auto extension : extensions)
            {
                if (EndsWith(name, extension))
                {
                    files.push_back(folder + L"\\" + name);
                    break;
                }
            }
        } while (FindNextFileW(find, &data));
        FindClose(find);
        return files;
    }

    HRESULT ReadIdentity(IAppxManifestPackageId* id, setup::PackageFile& file)
    {
        LPWSTR text = nullptr;
        HRESULT hr = id->GetName(&text);
        if (FAILED(hr)) return hr;
        file.Name = TakeString(text);

        if (FAILED(hr = id->GetPublisher(&text))) return hr;
        file.Publisher = TakeString(text);

        if (FAILED(hr = id->GetPackageFamilyName(&text))) return hr;
        file.FamilyName = TakeString(text);

        if (FAILED(hr = id->GetVersion(&file.Version))) return hr;

        APPX_PACKAGE_ARCHITECTURE architecture = APPX_PACKAGE_ARCHITECTURE_NEUTRAL;
        if (FAILED(hr = id->GetArchitecture(&architecture))) return hr;
        file.Architecture = static_cast<uint32_t>(architecture);
        return S_OK;
    }

    HRESULT ReadPackage(std::wstring const& path, setup::PackageFile& file)
    {
        file.Path = path;

        Com<IStream> stream;
        HRESULT hr = SHCreateStreamOnFileEx(path.c_str(), STGM_READ | STGM_SHARE_DENY_WRITE, 0, FALSE, nullptr, stream.put());
        if (FAILED(hr)) return hr;

        Com<IAppxManifestPackageId> id;
        if (EndsWith(path, L"bundle"))
        {
            Com<IAppxBundleFactory> factory;
            Com<IAppxBundleReader> reader;
            Com<IAppxBundleManifestReader> manifest;
            if (FAILED(hr = CoCreateInstance(__uuidof(AppxBundleFactory), nullptr, CLSCTX_INPROC_SERVER, __uuidof(IAppxBundleFactory), factory.put_void()))) return hr;
            if (FAILED(hr = factory->CreateBundleReader(stream.get(), reader.put()))) return hr;
            if (FAILED(hr = reader->GetManifest(manifest.put()))) return hr;
            if (FAILED(hr = manifest->GetPackageId(id.put()))) return hr;
        }
        else
        {
            Com<IAppxFactory> factory;
            Com<IAppxPackageReader> reader;
            Com<IAppxManifestReader> manifest;
            if (FAILED(hr = CoCreateInstance(__uuidof(AppxFactory), nullptr, CLSCTX_INPROC_SERVER, __uuidof(IAppxFactory), factory.put_void()))) return hr;
            if (FAILED(hr = factory->CreatePackageReader(stream.get(), reader.put()))) return hr;
            if (FAILED(hr = reader->GetManifest(manifest.put()))) return hr;
            if (FAILED(hr = manifest->GetPackageId(id.put()))) return hr;
        }
        return ReadIdentity(id.get(), file);
    }

    uint32_t NativeArchitecture()
    {
        USHORT process = 0, native = 0;
        if (IsWow64Process2(GetCurrentProcess(), &process, &native))
        {
            switch (native)
            {
            case IMAGE_FILE_MACHINE_ARM64: return APPX_PACKAGE_ARCHITECTURE_ARM64;
            case IMAGE_FILE_MACHINE_I386: return APPX_PACKAGE_ARCHITECTURE_X86;
            case IMAGE_FILE_MACHINE_ARMNT: return APPX_PACKAGE_ARCHITECTURE_ARM;
            default: break;
            }
        }
        return APPX_PACKAGE_ARCHITECTURE_X64;
    }

    wchar_t const* ArchitectureFolder(uint32_t architecture)
    {
        switch (architecture)
        {
        case APPX_PACKAGE_ARCHITECTURE_ARM64: return L"arm64";
        case APPX_PACKAGE_ARCHITECTURE_X86: return L"x86";
        case APPX_PACKAGE_ARCHITECTURE_ARM: return L"arm";
        default: return L"x64";
        }
    }

    bool SignatureTrusted(std::wstring const& path)
    {
        WINTRUST_FILE_INFO file{ sizeof(file) };
        file.pcwszFilePath = path.c_str();

        GUID action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
        WINTRUST_DATA data{ sizeof(data) };
        data.dwUIChoice = WTD_UI_NONE;
        data.fdwRevocationChecks = WTD_REVOKE_NONE;
        data.dwUnionChoice = WTD_CHOICE_FILE;
        data.pFile = &file;
        data.dwStateAction = WTD_STATEACTION_VERIFY;
        data.dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL;

        LONG status = WinVerifyTrust(static_cast<HWND>(INVALID_HANDLE_VALUE), &action, &data);
        data.dwStateAction = WTD_STATEACTION_CLOSE;
        WinVerifyTrust(static_cast<HWND>(INVALID_HANDLE_VALUE), &action, &data);

        setup::Log(L"Signature check of the package: 0x%08X", static_cast<unsigned>(status));
        return status == ERROR_SUCCESS;
    }

    HCERTSTORE OpenMachineStore(wchar_t const* name, bool write)
    {
        DWORD flags = CERT_SYSTEM_STORE_LOCAL_MACHINE | (write ? 0 : CERT_STORE_READONLY_FLAG);
        return CertOpenStore(CERT_STORE_PROV_SYSTEM_W, 0, 0, flags, name);
    }

    PCCERT_CONTEXT FindByHash(HCERTSTORE store, BYTE* hash, DWORD size)
    {
        CRYPT_HASH_BLOB blob{ size, hash };
        return CertFindCertificateInStore(store, X509_ASN_ENCODING | PKCS_7_ASN_ENCODING, 0, CERT_FIND_SHA1_HASH, &blob, nullptr);
    }

    bool StoreContains(wchar_t const* name, BYTE* hash, DWORD size)
    {
        HCERTSTORE store = OpenMachineStore(name, false);
        if (!store) return false;
        PCCERT_CONTEXT found = FindByHash(store, hash, size);
        if (found) CertFreeCertificateContext(found);
        CertCloseStore(store, 0);
        return found != nullptr;
    }

    void WriteMarker(BYTE* hash, DWORD size)
    {
        HKEY key = nullptr;
        if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, MarkerKey, 0, nullptr, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, nullptr, &key, nullptr) == ERROR_SUCCESS)
        {
            RegSetValueExW(key, L"Certificate", 0, REG_BINARY, hash, size);
            RegCloseKey(key);
        }
    }

    bool KeyIsEmpty(HKEY root, wchar_t const* path)
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(root, path, 0, KEY_READ | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return false;
        DWORD subKeys = 1, values = 1;
        RegQueryInfoKeyW(key, nullptr, nullptr, nullptr, &subKeys, nullptr, nullptr, &values, nullptr, nullptr, nullptr, nullptr);
        RegCloseKey(key);
        return subKeys == 0 && values == 0;
    }

    void Run(std::wstring commandLine)
    {
        STARTUPINFOW startup{ sizeof(startup) };
        startup.dwFlags = STARTF_USESHOWWINDOW;
        startup.wShowWindow = SW_HIDE;
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process))
        {
            setup::Log(L"Could not run %s: %u", commandLine.c_str(), GetLastError());
            return;
        }
        WaitForSingleObject(process.hProcess, 5 * 60 * 1000);
        DWORD exitCode = 0;
        GetExitCodeProcess(process.hProcess, &exitCode);
        setup::Log(L"%s exited with %u", commandLine.c_str(), exitCode);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }

    bool LaunchThroughShell(std::wstring const& target)
    {
        // asking the desktop's shell to start the app makes it run as the signed-in user, not elevated
        Com<IShellWindows> windows;
        if (FAILED(CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER, IID_IShellWindows, windows.put_void()))) return false;

        VARIANT location{};
        VARIANT root{};
        long hwnd = 0;
        Com<IDispatch> desktop;
        if (windows->FindWindowSW(&location, &root, SWC_DESKTOP, &hwnd, SWFO_NEEDDISPATCH, desktop.put()) != S_OK || !desktop) return false;

        Com<IServiceProvider> provider;
        Com<IShellBrowser> browser;
        Com<IShellView> view;
        Com<IDispatch> background;
        Com<IShellFolderViewDual> folderView;
        Com<IDispatch> application;
        Com<IShellDispatch2> shell;
        if (FAILED(desktop->QueryInterface(IID_IServiceProvider, provider.put_void()))) return false;
        if (FAILED(provider->QueryService(SID_STopLevelBrowser, IID_IShellBrowser, browser.put_void()))) return false;
        if (FAILED(browser->QueryActiveShellView(view.put()))) return false;
        if (FAILED(view->GetItemObject(SVGIO_BACKGROUND, IID_IDispatch, background.put_void()))) return false;
        if (FAILED(background->QueryInterface(IID_IShellFolderViewDual, folderView.put_void()))) return false;
        if (FAILED(folderView->get_Application(application.put()))) return false;
        if (FAILED(application->QueryInterface(IID_IShellDispatch2, shell.put_void()))) return false;

        BSTR file = SysAllocString(target.c_str());
        VARIANT empty{};
        HRESULT hr = shell->ShellExecute(file, empty, empty, empty, empty);
        SysFreeString(file);
        return SUCCEEDED(hr);
    }
}

namespace setup
{
    Strings const& Text()
    {
        static Strings const& text = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_POLISH ? Polish : English;
        return text;
    }

    std::wstring ModuleFolder()
    {
        wchar_t path[MAX_PATH * 4];
        DWORD length = GetModuleFileNameW(nullptr, path, ARRAYSIZE(path));
        std::wstring folder(path, length);
        return folder.substr(0, folder.find_last_of(L'\\'));
    }

    long LoadPayload(Payload& payload)
    {
        std::wstring folder = ModuleFolder() + L"\\Payload";

        auto apps = FindFiles(folder, { L".msixbundle", L".appxbundle", L".msix", L".appx" });
        if (apps.empty())
        {
            Log(L"No package found in %s", folder.c_str());
            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        }

        HRESULT hr = ReadPackage(apps.front(), payload.App);
        if (FAILED(hr))
        {
            Log(L"Could not read %s: 0x%08X", apps.front().c_str(), static_cast<unsigned>(hr));
            return hr;
        }
        Log(L"Package %s %s (%s)", payload.App.Name.c_str(), VersionString(payload.App.Version).c_str(), payload.App.FamilyName.c_str());

        auto certificates = FindFiles(folder, { L".cer" });
        if (!certificates.empty()) payload.Certificate = certificates.front();

        uint32_t architecture = NativeArchitecture();
        std::wstring dependencies = folder + L"\\Dependencies";
        std::wstring specific = dependencies + L"\\" + ArchitectureFolder(architecture);
        bool perArchitecture = FolderExists(specific);

        for (auto const& path : FindFiles(perArchitecture ? specific : dependencies, { L".appx", L".msix" }))
        {
            PackageFile dependency;
            if (FAILED(hr = ReadPackage(path, dependency)))
            {
                Log(L"Could not read %s: 0x%08X", path.c_str(), static_cast<unsigned>(hr));
                return hr;
            }
            if (!perArchitecture && dependency.Architecture != architecture && dependency.Architecture != APPX_PACKAGE_ARCHITECTURE_NEUTRAL)
            {
                continue;
            }
            Log(L"Dependency %s %s", dependency.Name.c_str(), VersionString(dependency.Version).c_str());
            payload.Dependencies.push_back(std::move(dependency));
        }
        return S_OK;
    }

    std::wstring VersionString(uint64_t version)
    {
        wchar_t text[64];
        swprintf_s(text, L"%u.%u.%u.%u",
                   static_cast<unsigned>((version >> 48) & 0xFFFF), static_cast<unsigned>((version >> 32) & 0xFFFF),
                   static_cast<unsigned>((version >> 16) & 0xFFFF), static_cast<unsigned>(version & 0xFFFF));
        return text;
    }

    std::wstring HResultString(long hr)
    {
        wchar_t text[16];
        swprintf_s(text, L"0x%08X", static_cast<unsigned>(hr));
        return text;
    }

    std::wstring Format(wchar_t const* text, std::wstring const& a, std::wstring const& b)
    {
        std::wstring result = text;
        size_t pos;
        if ((pos = result.find(L"%1")) != std::wstring::npos) result.replace(pos, 2, a);
        if ((pos = result.find(L"%2")) != std::wstring::npos) result.replace(pos, 2, b);
        return result;
    }

    long TrustCertificate(Payload const& payload, bool& added)
    {
        added = false;
        if (payload.Certificate.empty() || SignatureTrusted(payload.App.Path)) return S_OK;

        PCCERT_CONTEXT certificate = nullptr;
        if (!CryptQueryObject(CERT_QUERY_OBJECT_FILE, payload.Certificate.c_str(), CERT_QUERY_CONTENT_FLAG_CERT,
                              CERT_QUERY_FORMAT_FLAG_ALL, 0, nullptr, nullptr, nullptr, nullptr, nullptr,
                              reinterpret_cast<void const**>(&certificate)))
        {
            DWORD error = GetLastError();
            Log(L"Could not read the certificate: %u", error);
            return HRESULT_FROM_WIN32(error);
        }

        BYTE hash[20];
        DWORD hashSize = sizeof(hash);
        CertGetCertificateContextProperty(certificate, CERT_SHA1_HASH_PROP_ID, hash, &hashSize);

        HRESULT result = S_OK;
        if (StoreContains(L"Root", hash, hashSize))
        {
            Log(L"The certificate is already a trusted root");
        }
        else if (StoreContains(L"TrustedPeople", hash, hashSize))
        {
            // most likely left by the old MSI installer; take it over so uninstalling cleans it up
            Log(L"The certificate is already in Trusted People");
            WriteMarker(hash, hashSize);
        }
        else
        {
            HCERTSTORE store = OpenMachineStore(L"TrustedPeople", true);
            if (store && CertAddCertificateContextToStore(store, certificate, CERT_STORE_ADD_USE_EXISTING, nullptr))
            {
                Log(L"Added the certificate to Trusted People");
                WriteMarker(hash, hashSize);
                added = true;
            }
            else
            {
                result = HRESULT_FROM_WIN32(GetLastError());
                Log(L"Could not add the certificate: 0x%08X", static_cast<unsigned>(result));
            }
            if (store) CertCloseStore(store, 0);
        }

        CertFreeCertificateContext(certificate);
        return result;
    }

    void RemoveOurCertificate()
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, MarkerKey, 0, KEY_READ | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return;

        BYTE hash[20];
        DWORD size = sizeof(hash);
        DWORD type = 0;
        LSTATUS status = RegQueryValueExW(key, L"Certificate", nullptr, &type, hash, &size);
        RegCloseKey(key);

        if (status == ERROR_SUCCESS && type == REG_BINARY && size == sizeof(hash))
        {
            HCERTSTORE store = OpenMachineStore(L"TrustedPeople", true);
            if (store)
            {
                PCCERT_CONTEXT found = FindByHash(store, hash, size);
                if (found)
                {
                    // CertDeleteCertificateFromStore also frees the context
                    bool deleted = CertDeleteCertificateFromStore(found) != FALSE;
                    Log(L"Removed the certificate from Trusted People: %d", deleted ? 1 : 0);
                }
                CertCloseStore(store, 0);
            }
        }

        RegDeleteKeyExW(HKEY_LOCAL_MACHINE, MarkerKey, KEY_WOW64_64KEY, 0);
        if (KeyIsEmpty(HKEY_LOCAL_MACHINE, VendorKey)) RegDeleteKeyExW(HKEY_LOCAL_MACHINE, VendorKey, KEY_WOW64_64KEY, 0);
    }

    void AllowSideloading()
    {
        wchar_t const path[] = L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock";
        HKEY key = nullptr;
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_QUERY_VALUE | KEY_SET_VALUE | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return;

        DWORD value = 1, size = sizeof(value), type = 0;
        if (RegQueryValueExW(key, L"AllowAllTrustedApps", nullptr, &type, reinterpret_cast<BYTE*>(&value), &size) == ERROR_SUCCESS &&
            type == REG_DWORD && value == 0)
        {
            value = 1;
            RegSetValueExW(key, L"AllowAllTrustedApps", 0, REG_DWORD, reinterpret_cast<BYTE const*>(&value), sizeof(value));
            Log(L"Turned on sideloading");
        }
        RegCloseKey(key);
    }

    void RemoveLegacyMsi()
    {
        std::vector<std::wstring> products;
        wchar_t code[39];
        for (DWORD index = 0; MsiEnumRelatedProductsW(LegacyUpgradeCode, 0, index, code) == ERROR_SUCCESS; ++index)
        {
            products.emplace_back(code);
        }

        wchar_t system[MAX_PATH];
        GetSystemDirectoryW(system, MAX_PATH);
        for (auto const& product : products)
        {
            Log(L"Removing the old MSI installation %s", product.c_str());
            Run(L"\"" + std::wstring(system) + L"\\msiexec.exe\" /x " + product + L" /qn REBOOT=ReallySuppress");
        }
    }

    void LaunchApp(std::wstring const& appUserModelId)
    {
        std::wstring target = L"shell:AppsFolder\\" + appUserModelId;
        if (LaunchThroughShell(target))
        {
            Log(L"Started %s", appUserModelId.c_str());
            return;
        }

        // a new explorer.exe hands the request to the running shell, which isn't elevated either
        wchar_t windows[MAX_PATH];
        GetWindowsDirectoryW(windows, MAX_PATH);
        std::wstring explorer = std::wstring(windows) + L"\\explorer.exe";
        ShellExecuteW(nullptr, nullptr, explorer.c_str(), target.c_str(), nullptr, SW_SHOWNORMAL);
        Log(L"Started %s through explorer.exe", appUserModelId.c_str());
    }

    bool HasSwitch(wchar_t const* name)
    {
        int count = 0;
        LPWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &count);
        if (!arguments) return false;
        bool found = false;
        for (int i = 1; i < count && !found; ++i)
        {
            wchar_t const* argument = arguments[i];
            if (*argument == L'/' || *argument == L'-') ++argument;
            found = _wcsicmp(argument, name) == 0;
        }
        LocalFree(arguments);
        return found;
    }

    int& ExitCode()
    {
        static int code = 0;
        return code;
    }

    std::wstring LogPath()
    {
        wchar_t temp[MAX_PATH];
        GetTempPathW(MAX_PATH, temp);
        return std::wstring(temp) + L"UltraPadSetup.log";
    }

    void Log(wchar_t const* format, ...)
    {
        static SRWLOCK lock = SRWLOCK_INIT;
        static bool first = true;

        wchar_t message[2048];
        va_list args;
        va_start(args, format);
        _vsnwprintf_s(message, _TRUNCATE, format, args);
        va_end(args);

        SYSTEMTIME now;
        GetLocalTime(&now);
        wchar_t line[2200];
        int length = swprintf_s(line, L"%02u:%02u:%02u.%03u  %s\r\n", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, message);
        OutputDebugStringW(line);

        AcquireSRWLockExclusive(&lock);
        HANDLE file = CreateFileW(LogPath().c_str(), first ? GENERIC_WRITE : FILE_APPEND_DATA, FILE_SHARE_READ, nullptr,
                                  first ? CREATE_ALWAYS : OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE)
        {
            int bytes = WideCharToMultiByte(CP_UTF8, 0, line, length, nullptr, 0, nullptr, nullptr);
            std::string utf8(bytes, '\0');
            WideCharToMultiByte(CP_UTF8, 0, line, length, utf8.data(), bytes, nullptr, nullptr);
            DWORD written = 0;
            WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
            CloseHandle(file);
            first = false;
        }
        ReleaseSRWLockExclusive(&lock);
    }
}
