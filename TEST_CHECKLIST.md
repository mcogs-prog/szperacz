# Arkusz Scenariuszy Testowych — Szperacz 1.5.0 RC (OGŚ)

Cykl testowy: **Do końca bieżącego tygodnia**  
Środowisko: **Windows 10 / Windows 11 (x64)**

---

### 1. Stabilność i Odporność (Crash Resistance)
- [ ] **Katalogi zablokowane:** Wejście do folderów z brakiem uprawnień (np. `C:\System Volume Information` lub `$Recycle.Bin`). Aplikacja nie powinna się wyłączyć; status powinien zasygnalizować brak uprawnień.
- [ ] **Dyski wymienne:** Podłączenie pendrive'a USB w trakcie działania programu i kliknięcie „Odśwież”. Sprawdzenie, czy nowa litera dysku pojawia się poprawnie.
- [ ] **Długie ścieżki:** Wejście w głęboko zagnieżdżone drzewo katalogów (>260 znaków).

### 2. Klawiszologia i Podstawowe Operacje I/O
- [ ] **Nawigacja bez myszy:** Płynne przełączanie paneli klawiszem `Tab`, wejście do katalogu (`Enter`), wyjście (`Backspace` lub `..`).
- [ ] **Zaznaczanie wzorcem:** Naciśnięcie `Num +`, wpisanie `*.png` i sprawdzenie czy zaznaczono tylko odpowiednie pliki.
- [ ] **Zaznaczanie punktowe:** Klawisze `Insert` / `Spacja` (selekcja i skok do wiersza poniżej).
- [ ] **Kopiowanie / Przenoszenie (`F5` / `F6`):** Transfer pliku o rozmiarze >1 GB; weryfikacja płynności paska postępu.
- [ ] **Bezpieczne usuwanie (`F8` / `Delete`):** Usunięcie pliku testowego, sprawdzenie czy trafił do systemowego Kosza Windows i czy można go przywrócić.

### 3. Narzędzia i Moduły Specjalne
- [ ] **Wielokrotna zmiana nazw (`Ctrl + M`):** Zaznaczenie 10 plików, ustawienie maski `[N]_[C]`, sprawdzenie podglądu i zatwierdzenie.
- [ ] **Wbudowany Lister (`F3`):**
  - Plik graficzny (`.png` lub `.jpg`) -> podgląd obrazu z rozdzielczością.
  - Plik wykonywalny (`.exe` lub `.dll`) -> widok szesnastkowy Hex.
  - Plik źródłowy (`.cs` lub `.txt`) -> widok tekstowy.
- [ ] **Wyszukiwarka (`Alt + F7`):** Szukanie pliku po nazwie i po fragmencie zawartego tekstu; sprawdzenie przycisku „Przejdź do pliku”.
- [ ] **Obsługa ZIP:** Spakowanie folderu klawiszem `Alt + F5` oraz rozpakowanie pod `Alt + F9`.
- [ ] **Suma kontrolna:** Wyliczenie skrótu SHA-256 dla pliku i wklejenie ze schowka (`Ctrl + V`).

### 4. Pasek Poleceń CLI i Integracja
- [ ] **Wiersz poleceń:** Wpisanie komendy (np. `mkdir test_ogś` lub `ping 127.0.0.1`) w dolnym pasku CLI i naciśnięcie Enter.
- [ ] **Skrót terminala:** Uruchomienie terminala pod `Ctrl + ~` (sprawdzenie bieżącej ścieżki startowej).
- [ ] **Menu kontekstowe:** Prawoklik na pliku i wywołanie opcji „Kopiuj pełną ścieżkę”.

### 5. Sesja i Zapamiętywanie Stanu (Persistence)
- [ ] Otwórz 3 różne karty w lewym panelu i 2 w prawym.
- [ ] Dodaj folder do ulubionych (`Ctrl + D`).
- [ ] Zamknij aplikację i uruchom ponownie.
- [ ] Zweryfikuj, czy wszystkie karty, ścieżki i ulubione zostały w pełni odtworzone z pliku `config.json`.
