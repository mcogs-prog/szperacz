# Szperacz — wersja 1.5.0 RC (Release Candidate)

**Nazwa programu:** Szperacz  
**Twórca / wydawca:** OGŚ  
**Wersja:** 1.5.0-RC  
**System:** Windows 10 i Windows 11 (x64)

Zaawansowany, dwupanelowy menedżer plików w stylu Total Commandera stworzony w .NET 8 WPF. Wydanie przygotowane do tygodniowego cyklu testów stabilnościowych.

## Nowości w wydaniu 1.5.0 RC
1. **Zapamiętywanie stanu sesji (Persistence):**
   - Zapis i odczyt konfiguracji do `%APPDATA%\OGS\Szperacz\config.json`.
   - Zapamiętywanie otwartych zakładek (Tabs), aktywnego panelu, ulubionych ścieżek oraz rozmiaru/stanu okna.
2. **Dedykowana ikona programu (`Szperacz.ico`):**
   - Natywna wielowarstwowa ikona projektu wbudowana w plik `.exe`, okno WPF oraz instalator Inno Setup.
3. **Odporność na brak uprawnień I/O:**
   - Obsługa wyjątków `UnauthorizedAccessException` – brak awarii przy wchodzeniu do zablokowanych folderów systemowych (np. `System Volume Information`).
4. **Wbudowany arkusz testowy:**
   - Szczegółowa checklista przypadków testowych znajduje się w pliku `TEST_CHECKLIST.md`.

## Budowanie instalatora
W katalogu głównym projektu uruchom w PowerShellu:
```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-installer.ps1
```
Gotowy instalator `Szperacz-Setup-1.5.0-RC-x64.exe` zostanie wygenerowany w folderze `dist`.
