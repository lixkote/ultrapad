#pragma once

namespace setup
{
    struct Strings
    {
        wchar_t const* WindowTitle;

        wchar_t const* InstallTitle;
        wchar_t const* InstallText;        // %1 = version
        wchar_t const* UpdateTitle;
        wchar_t const* UpdateText;         // %1 = installed version, %2 = new version
        wchar_t const* InstalledTitle;
        wchar_t const* InstalledText;      // %1 = version
        wchar_t const* NewerTitle;
        wchar_t const* NewerText;          // %1 = installed version, %2 = this version
        wchar_t const* StoreTitle;
        wchar_t const* StoreText;
        wchar_t const* Description;

        wchar_t const* Install;
        wchar_t const* Update;
        wchar_t const* Reinstall;
        wchar_t const* Downgrade;
        wchar_t const* Uninstall;
        wchar_t const* Cancel;
        wchar_t const* Close;
        wchar_t const* Finish;
        wchar_t const* TryAgain;
        wchar_t const* OpenLog;

        wchar_t const* Preparing;
        wchar_t const* Certificate;
        wchar_t const* Installing;
        wchar_t const* InstallingTitle;
        wchar_t const* UpdatingTitle;
        wchar_t const* UninstallingTitle;
        wchar_t const* Removing;
        wchar_t const* Cleaning;

        wchar_t const* DoneTitle;
        wchar_t const* DoneText;
        wchar_t const* UninstalledTitle;
        wchar_t const* UninstalledText;
        wchar_t const* LaunchApp;
        wchar_t const* UninstallHint;

        wchar_t const* InstallFailed;
        wchar_t const* UninstallFailed;
        wchar_t const* PayloadMissing;
        wchar_t const* ErrorCode;          // %1 = hex code
        wchar_t const* ConfirmUninstall;
        wchar_t const* ConfirmUninstallText;
    };

    inline constexpr Strings English
    {
        L"UltraPad Setup",

        L"Install UltraPad",
        L"UltraPad %1 will be installed for the current Windows user.",
        L"Update UltraPad",
        L"UltraPad %1 is installed. Setup will update it to version %2. Your documents and settings are kept.",
        L"UltraPad is already installed",
        L"Version %1 is installed. You can repair it by installing it again, or uninstall it.",
        L"A newer UltraPad is installed",
        L"Version %1 is installed, which is newer than the version in this setup (%2).",
        L"UltraPad from Microsoft Store is installed",
        L"It gets its updates from Microsoft Store, so there is nothing to install. You can uninstall it here if you want to switch to this version.",
        L"A modern word processor with the classic WordPad feel. It can open and save RTF, DOCX, ODT and plain text documents.",

        L"Install",
        L"Update",
        L"Reinstall",
        L"Install older version",
        L"Uninstall",
        L"Cancel",
        L"Close",
        L"Finish",
        L"Try again",
        L"Open log",

        L"Getting ready…",
        L"Trusting the UltraPad certificate…",
        L"Installing UltraPad…",
        L"Installing UltraPad",
        L"Updating UltraPad",
        L"Uninstalling UltraPad",
        L"Removing UltraPad…",
        L"Cleaning up…",

        L"UltraPad is ready",
        L"UltraPad was installed. You can find it in the Start menu, and it shows up in “Open with” for RTF, DOCX, ODT and TXT files.",
        L"UltraPad was uninstalled",
        L"UltraPad and its file associations were removed from this PC.",
        L"Launch UltraPad",
        L"To uninstall UltraPad later, right-click it in the Start menu and choose Uninstall, or remove it in Settings > Apps > Installed apps. You don't need any registry cleaners or uninstall tools for it; they can damage Windows.",

        L"UltraPad couldn't be installed",
        L"UltraPad couldn't be uninstalled",
        L"The setup files are incomplete. Please download the installer again.",
        L"Error code: %1",
        L"Uninstall UltraPad?",
        L"UltraPad and its settings will be removed. Your documents are not affected.",
    };

    inline constexpr Strings Polish
    {
        L"Instalator UltraPad",

        L"Zainstaluj UltraPad",
        L"UltraPad %1 zostanie zainstalowany dla bieżącego użytkownika systemu Windows.",
        L"Zaktualizuj UltraPad",
        L"Zainstalowano UltraPad %1. Instalator zaktualizuje go do wersji %2. Dokumenty i ustawienia zostaną zachowane.",
        L"UltraPad jest już zainstalowany",
        L"Zainstalowana jest wersja %1. Możesz ją naprawić, instalując ponownie, albo ją odinstalować.",
        L"Zainstalowano nowszy UltraPad",
        L"Zainstalowana jest wersja %1, nowsza niż wersja w tym instalatorze (%2).",
        L"Zainstalowano UltraPad ze sklepu Microsoft Store",
        L"Aktualizacje pobiera ze sklepu Microsoft Store, więc nie ma czego instalować. Jeśli chcesz przejść na tę wersję, możesz go tutaj odinstalować.",
        L"Nowoczesny edytor tekstu w stylu klasycznego WordPada. Otwiera i zapisuje dokumenty RTF, DOCX, ODT i zwykły tekst.",

        L"Zainstaluj",
        L"Zaktualizuj",
        L"Zainstaluj ponownie",
        L"Zainstaluj starszą wersję",
        L"Odinstaluj",
        L"Anuluj",
        L"Zamknij",
        L"Zakończ",
        L"Spróbuj ponownie",
        L"Otwórz dziennik",

        L"Przygotowywanie…",
        L"Dodawanie certyfikatu UltraPad…",
        L"Instalowanie UltraPad…",
        L"Instalowanie UltraPad",
        L"Aktualizowanie UltraPad",
        L"Odinstalowywanie UltraPad",
        L"Usuwanie UltraPad…",
        L"Sprzątanie…",

        L"UltraPad jest gotowy",
        L"UltraPad został zainstalowany. Znajdziesz go w menu Start, a w menu „Otwórz za pomocą” pojawi się dla plików RTF, DOCX, ODT i TXT.",
        L"UltraPad został odinstalowany",
        L"UltraPad i jego skojarzenia plików zostały usunięte z tego komputera.",
        L"Uruchom UltraPad",
        L"Aby później odinstalować UltraPad, kliknij go prawym przyciskiem myszy w menu Start i wybierz Odinstaluj albo usuń go w Ustawienia > Aplikacje > Zainstalowane aplikacje. Nie potrzebujesz do tego żadnych programów do czyszczenia rejestru ani deinstalatorów, mogą one uszkodzić system Windows.",

        L"Nie udało się zainstalować UltraPad",
        L"Nie udało się odinstalować UltraPad",
        L"Pliki instalatora są niekompletne. Pobierz instalator ponownie.",
        L"Kod błędu: %1",
        L"Odinstalować UltraPad?",
        L"UltraPad i jego ustawienia zostaną usunięte. Twoje dokumenty pozostaną bez zmian.",
    };

    Strings const& Text();
}
