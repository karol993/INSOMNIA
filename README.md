# Insomnia

[![Downloads](https://img.shields.io/github/downloads/karol993/INSOMNIA/total.svg?style=flat-square&color=blue)](https://github.com/karol993/INSOMNIA/releases)
[![Release](https://img.shields.io/github/v/release/karol993/INSOMNIA?style=flat-square&color=success)](https://github.com/karol993/INSOMNIA/releases/latest)

C# / Windows Forms / .NET Framework 4.8, Windows 10 i 11. Uruchom plik
`Insomnia.exe` z paczki ZIP. Menu ikony zasobnika i jej dwuklik otwierają
ustawienia. Nie potrzeba uruchamiania jako administrator.

## Ustawienia i funkcje

Okno ustawień podzielone jest na czytelne karty:

### Karta 1: Harmonogram i Wi-Fi
- **Autostart z Windows:** opcja automatycznego startu programu w tle przy logowaniu użytkownika (rejestr `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, bez potrzeby uprawnień administratora).
- **Harmonogram:** godziny i dni tygodnia (Pn–Nd). Początek włącznie, koniec wyłącznie; zakres przez północ jest obsługiwany (przypisywany do zmiany z wybranego dnia), identyczne godziny oznaczają całą dobę.
- **Reguły Wi-Fi:** wybór zachowania programu:
  - *Wyłączaj program, gdy wykryto sieć z listy* (np. w biurze).
  - *Działaj tylko wtedy, gdy wykryto sieć z listy* (np. tylko w domu).
- **Zarządzanie listą SSID:** Dodaj/Usuń zmieniają listę roboczą. Wpisany tekst trzeba zatwierdzić przez Dodaj lub dodać jednym kliknięciem z listy wykrytych sieci w zasięgu.
- **Diagnostyka adapterów:** podgląd aktualnie wykrytych sieci w zasięgu, status skanowania i bezpośredni skrót do ustawień uprawnień lokalizacji Windows.

### Karta 2: Symulacja i Zgodność
- **Wybór działań symulacji:** użytkownik decyduje, jakie impulsy generuje program (losowane z włączonych opcji co ok. 30 s po 25 s bezczynności):
  - *Mikro-ruchy myszą* (płynny micro-jitter kursora o kilka pikseli)
  - *Niewidoczny klawisz F15* (kod wirtualny `0x7E`, nie wpisuje znaków i nie koliduje z żadnymi skrótami w Windows, Office ani CAD)
  - *Kółko myszy* (dyskretny scroll w dół i w górę)
  - *Przełączanie okien Alt+Tab* (domyślnie wyłączone; przydatne przy narzędziach monitorujących nazwy aktywnych okien)
- **Wspierane aplikacje i skuteczność:**
  - 🟢 **Microsoft Teams (Desktop & Web)** — stały zielony status „Dostępny” (Available), brak przechodzenia w „Zaraz wracam” (Away).
  - 🟢 **Slack / Skype / Zoom / Webex** — reset liczników nieaktywności, stały status online.
  - 🟢 **Blokada ekranu Windows (GPO / Intune)** — brak uśpienia i wygaszacza ekranu dzięki impulsom wejściowym i `EXECUTION_STATE`.
  - 🟡 **Narzędzia ewidencji czasu (Time Doctor, Hubstaff, DeskTime)** — rejestrowanie aktywności myszy/klawiatury (oraz okien przy włączonym Alt+Tab).

- **Transakcyjny zapis:** Zastosuj zapisuje na dysku i aktualizuje działanie bez zamykania okna. Zapisz i zamknij zamyka dopiero po udanym zapisie. Anuluj odrzuca zmiany od ostatniego zapisu. Krzyżyk pyta o zapis/odrzucenie/powrót.
- **Spójność stanu:** Przełączenie ręcznego stanu w zasobniku podczas edycji w oknie ustawień jest zawsze zachowywane.

Konfiguracja nadal używa Properties.Settings i standardowego pliku user.config
w profilu użytkownika. Ścieżka jest zależna m.in. od położenia EXE i tożsamości
aplikacji. Przeniesienie programu do innego katalogu może utworzyć oddzielną
konfigurację — restart testuj z tego samego pliku EXE. Błąd odczytu nie powoduje
cichego nadpisania uszkodzonego pliku.

## Wi-Fi

Skan startuje przy uruchomieniu, co 45 sekund oraz na żądanie. Wszystkie adaptery
są odpytywane przez Windows WLAN API. Powiadomienia ACM są przypisane do GUID
adaptera; timeout wynosi 12 sekund. Native WLAN session żyje do zakończenia
wszystkich operacji, także przy anulowaniu. Kolejne żądania współdzielą trwający skan.

Lista wykluczeń jest niezależna od wyników. Pomyślne wyniki każdego adaptera
mają ważność 2 minuty. Błąd nie usuwa ani nie odświeża ich czasu ważności.
Przy niepełnych/nieaktualnych danych wyświetlane jest ostrzeżenie.
Po wygaśnięciu wyników status zależy od ręcznego przełącznika i harmonogramu.
Przy braku sieci poprawnie zakończony skan zwraca pustą listę; błąd nigdy
nie jest prezentowany jako poprawny pusty skan.

Windows może wymagać zgody na lokalizację. Przycisk otwiera tylko stronę
ustawień; aplikacja nie zmienia uprawnień. Wyłączony WLAN AutoConfig, radio,
polityki systemowe lub sterownik mogą uniemożliwiać skanowanie.
Widoczność zależy od okresu skanowania i informacji sterownika — nie jest
ciągłym pomiarem. Ukryte SSID nie zawsze dają się poznać bez profilu.
SSID to maksymalnie 32 bajty; aplikacja obsługuje tekst UTF-8, w tym polskie znaki.
Nieprawidłowe sekwencje UTF-8 są pomijane, aby nie porównywać błędnie zdekodowanych nazw.

## Kompilacja i testy

Wymagane Visual Studio 2022 / Build Tools z MSBuild i targeting pack .NET Framework 4.8.

```powershell
.\build.ps1 -Test
.\Tests\bin\x64\Insomnia.Tests.exe --hardware
.\Tests\bin\x64\Insomnia.Tests.exe --ui
.\package.ps1
```

Build trafia do artifacts/Release. Testy używają oddzielnej tożsamości
Insomnia.Tests.exe i własnego user.config, a podgląd --ui ma konfigurację
wyłącznie w pamięci i nie symuluje aktywności. --hardware odczytuje prawdziwe
adaptery bez przełączania połączeń lub ustawień. package.ps1 zawsze buduje nowy
Release i tworzy artifacts/Insomnia-Fixed-Release.zip ze źródłami i binariami.
Szczegółowe wyniki i ograniczenia: TEST-REPORT.md.

## Dokumentacja Microsoft

- [WLAN_AVAILABLE_NETWORK](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/ns-wlanapi-wlan_available_network)
- [WlanScan](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanscan)
- [WlanRegisterNotification](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanregisternotification)
- [Dostęp do Wi-Fi i lokalizacji](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes)
