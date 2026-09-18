# Wyniki naprawy — 18.09.2026

## Przyczyny

1. Formularz miał zablokowany rozmiar 430×315 i nieokreślone wysokości wierszy.
   Przyciski zapisu mogły znaleźć się poza widoczną częścią okna.
2. Dodaj modyfikował tylko kontrolkę, a zapis następował dopiero po zamknięciu.
   To był błąd przepływu interfejsu, nie dowód awarii StringCollection.
3. WLAN_AVAILABLE_NETWORK był ucięty: odczyt następnych rekordów używał złego
   kroku pamięci. Poprawny rozmiar to 628 bajtów w x86 i x64.
4. Stałe opóźnienie nie potwierdzało zakończenia skanu; uchwyt mógł być zamykany
   podczas jego używania. Wyjątki i brak wyników były nierozróżnialne w interfejsie.
5. Timer aktywności korzystał z zapamiętanego statusu, a zapis formularza mógł
   nadpisywać nowszy stan ręcznego przełącznika.

## Zmiany

- SettingsForm.cs: nowy układ, przewijanie treści, stała stopka, edycja robocza,
  walidacja, zapis z obsługą błędów, diagnostyka i dodawanie wykrytych SSID.
- AppConfiguration.cs: osobny magazyn Properties.Settings i kontroler
  zatwierdzający stan dopiero po udanym Save. Sprawdzono zgodność plików
  Properties/Settings.settings i Settings.Designer.cs; pozostawiono StringCollection.
- WlanScanner.cs: kompletna struktura ABI, powiadomienia ACM per GUID,
  timeout/anulowanie, blokada równoległych żądań i bezpieczny czas życia sesji.
- WifiState.cs: osobny cache adapterów, ważność 2 minuty, rozróżnienie błędu
  od poprawnego braku sieci i ostrzeżenie o niepełnej kontroli.
- AppStatus.cs: jawny czas do obliczeń, priorytety i granice harmonogramu.
- TrayApplicationContext.cs i Program.cs: integracja, przeliczenie statusu
  bezpośrednio przed aktywnością, pojedynczy formularz, oczekiwanie na zakończenie skanu przy wyjściu.
- ActivitySimulation.cs: przeniesione bez zmiany wzorce z wersji zastanej przy tej naprawie.
- App.config, app.manifest, Insomnia.csproj: DPI PerMonitorV2, asInvoker i nowe pliki.
- Tests/, build.ps1, package.ps1, README.md oraz ten raport: odtwarzalne testy,
  budowanie i paczkowanie. AssemblyInfo.cs udostępnia wewnętrzne klasy testom.

## Wykonane

- Release z MSBuild Visual Studio 2022, .NET Framework 4.8: bez błędów i ostrzeżeń.
- Testy w osobnych procesach x86 i x64: po 9 grup testów.
- Rozmiar i wszystkie istotne offsety WLAN_AVAILABLE_NETWORK; kilka rekordów
  zbudowanych jako surowe bajty, bez flag połączenia/profilu; polskie znaki.
- Zapis przez formularz i Properties.Settings, zakończenie procesu i odczyt
  w nowym procesie; również zapis pustej listy, znaków specjalnych i godzin.
- Błąd zapisu, zachowanie szkicu, ponowienie, Anuluj po wcześniejszym Zastosuj,
  duplikaty i puste nazwy, zachowanie nowszego stanu ręcznego przełącznika.
- Harmonogram zwykły/nocny, północ, granice włącznie/wyłącznie, cała doba.
- Odmowa dostępu, timeout, brak adaptera, wyłączone radio, poprawny pusty wynik,
  kilka adapterów, wygaśnięcie cache oraz zamknięcie podczas skanu: testy z kontrolowanym źródłem danych.
- Formularze WinForms uruchomione na Windows: testy geometrii stopki przy
  programowym Scale 1.0, 1.25, 1.5, 2.0 i minimalnym rozmiarze okna.
- Rzeczywisty formularz obejrzany przez Computer Use na bieżącym pulpicie:
  widoczna stopka, polskie sekcje, nieaktywne godziny przy wyłączonym harmonogramie.
- Rzeczywiste WLAN: Intel Wi-Fi 6 AX201 160MHz, poprawne zakończenie skanu przez
  powiadomienie, odczyt wielu wyników. Test wykonywany bez tokenu administratora.
  Potwierdzono blokowanie statusu przez widoczne sieci z niezapaloną flagą
  WLAN_AVAILABLE_NETWORK_CONNECTED, bez łączenia komputera z nimi.

## Niewykonane na fizycznym sprzęcie

- Równoczesne użycie kilku fizycznych adapterów: dostępny był jeden.
- Odłączanie adaptera, wyłączenie radia, brak sieci w otoczeniu i systemowa
  odmowa lokalizacji: sprawdzone testami kontrolowanymi, nie przez zmianę ustawień użytkownika.
- Zmiana systemowego DPI 100/125/150/200% i przenoszenie pomiędzy monitorami
  z różnym DPI: nie wykonywano. Programowe Scale nie zastępuje tych testów.
- Oddzielna maszyna Windows 10: niewykonane.

## Krótka weryfikacja ręczna

1. Zamknij poprzednią Insomnię przez menu Wyjdź. Rozpakuj nowe Release do stałego
   katalogu i uruchom Insomnia.exe. Dodaj dwie sieci, Zastosuj, dodaj trzecią,
   Anuluj. Po ponownym uruchomieniu powinny pozostać tylko dwie.
2. Usuń wszystkie wpisy, Zapisz i zamknij, zakończ cały proces i uruchom ten sam
   EXE ponownie. Lista ma pozostać pusta. Sprawdź też pytanie przy krzyżyku.
3. Wyklucz sieć z listy wykrytych, z którą komputer nie jest połączony.
   Po skanie status ma być nieaktywny. Zniknięcie sieci nie usuwa wykluczenia.
4. Podłącz drugi adapter, powtórz odświeżenie i zamknij program w trakcie skanu.
   Wyłącz radio lub odłącz adaptery; sprawdź diagnostykę i wygaśnięcie wyników.
5. Przy odmowie dostępu użyj przycisku ustawień lokalizacji. Zgodę zmienia
   wyłącznie użytkownik; po zmianie ponów skan.
6. Sprawdź harmonogram 08:00–17:00, 22:00–06:00 oraz 08:00–08:00.
   Podczas edycji zmień ręczny stan w zasobniku, następnie Zastosuj — stan ma pozostać.
7. Ustaw kolejno DPI 100%, 125%, 150%, 200% na testowym Windows i otwórz okno.
   Przy minimalnym rozmiarze stopka ma pozostawać widoczna; przewija się tylko treść.

## Paczka

Nowy EXE pochodzi z artifacts/Release. Stary bin/Release/Insomnia.exe był
uruchomiony podczas naprawy i nie jest używany do przygotowania wydania.
ZIP zawiera Source/ i Release/, bez .git, .vs, obj, starych binariów i ustawień użytkownika.
