1. Opis funkcjonalności:

Projekt obejmuje rozwinięty system monitorowania produkcji, który umożliwia zbieranie danych z różnych maszyn poprzez połączenie z serwerem OPC UA. Dane te są następnie przesyłane do platformy Azure IoT Hub, gdzie są przetwarzane, analizowane i wykorzystywane do monitorowania wydajności produkcji oraz wykrywania awarii.

2. Sposoby komunikacji z platformą Azure:

Aplikacja komunikuje się z platformą Azure IoT Hub za pomocą protokołu MQTT. Wykorzystywane są wiadomości Device-to-Cloud (D2C) do przesyłania danych telemetrycznych oraz do wywoływania bezpośrednich metod na urządzeniach.

3. Format wiadomości D2C:

Wiadomości D2C zawierają dane telemetryczne, takie jak status produkcji, identyfikator zadania, produkcja dobra i wadliwa, temperatura oraz błędy urządzenia. Format danych jest JSON, który zawiera nazwę urządzenia oraz zestawienie parametrów wraz z ich wartościami.

4. Zawartość Device Twin:

Device Twin zawiera informacje o pożądanych i raportowanych właściwościach urządzenia. Właściwości pożądane określają produkcję docelową, podczas gdy właściwości raportowane zawierają aktualne dane związane z produkcją, temperaturą i błędami urządzenia.

5. Dostępne metody:

Aplikacja udostępnia dwie metody bezpośrednie na urządzeniach: EmergencyStop i ResetErrorStatus. EmergencyStop służy do natychmiastowego zatrzymania produkcji w przypadku awarii, natomiast ResetErrorStatus służy do resetowania statusu błędów urządzenia. 

6. Llogika biznesowa:

W przypadku, gdy urządzenie doświadcza więcej niż 3 błędów w ciągu 1 minuty, system natychmiast wywołuje metodę EmergencyStop na tym urządzeniu.
Jeśli urządzenie doświadcza spadku produkcji dobra poniżej 90%, system automatycznie zmniejsza pożądaną produkcję o 10 punktów procentowych.
