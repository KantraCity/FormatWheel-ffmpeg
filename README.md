# Format Wheel — расширение FFmpeg

Конвертация видео и аудио для [Format Wheel](https://github.com/KantraCity/FormatWheel) через FFmpeg.

- **Видео:** MP4, MOV, M4V, MKV, AVI, WEBM, WMV, FLV, 3GP, MTS, M2TS, TS, MPG, VOB, OGV, F4V, ASF, RM → MP4, HEVC, WEBM, MKV, MOV, AVI, WMV, GIF, MP3, M4A.
- **Аудио:** MP3, WAV, FLAC, M4A, AAC, OGG, OPUS, WMA, AIFF, AC3, DTS, AMR, APE, WV и др. → MP3, M4A, OPUS, OGG, WMA, FLAC, ALAC, WAV, AIFF.

## Слоты колеса

| Слот | Видео | Аудио |
|---|---|---|
| Shift | формат | формат |
| Ctrl+Shift | сжатие: обычно, лучше, сжать, сильно | сжатие (битрейт) |
| Alt+Shift | разрешение: 1080p, 720p, 480p, 360p (только уменьшение) | моно, «Выровнять» (громкость −14 LUFS, как у стримингов) |
| Ctrl+Alt+Shift | без звука | — |

Всё сочетается за один драг. FFmpeg работает с пониженным приоритетом, чтобы не тормозить систему.

## Установка

1. Сначала поставьте саму программу Format Wheel.
2. Скачайте и запустите `FormatWheel-FFmpeg-Setup-x.y.z.exe` из релизов. Он положит расширение в папку программы и, если FFmpeg ещё не установлен, поставит его через winget (`Gyan.FFmpeg`).
3. В Format Wheel откройте меню трея → «Расширения» → «Перезагрузить расширения» (или перезапустите программу).

Если winget недоступен, поставьте FFmpeg вручную: `winget install Gyan.FFmpeg`, затем перезагрузите расширения.

## Сборка

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Нужны [.NET 10 SDK](https://dotnet.microsoft.com/download) и пакет `FormatWheel.Sdk`. На nuget.org его пока нет: возьмите `FormatWheel.Sdk.x.y.z.nupkg` из [релизов FormatWheel-app](https://github.com/KantraCity/FormatWheel-app/releases) (или соберите `dotnet pack src/FormatWheel.Sdk` в репозитории программы) и подключите папку с ним как источник: `dotnet nuget add source C:\путь\к\папке -n formatwheel-sdk`. GitHub Actions делает это сам. Inno Setup скрипт поставит через winget, если его нет.

## Как это сделано

Класс реализует `IConverter` из `FormatWheel.Sdk`: на слот Shift кладёт лепестки форматов, в `Convert` запускает `ffmpeg.exe` и парсит его вывод (`Duration` и `time=`) для кольца прогресса. `IsSlow = true`: файл предлагается получателю, как только кодирование закончилось, а если его бросили раньше — программа доделает его и сохранит в папку, куда бросили. Это рабочий пример для своих расширений — см. `src/FormatWheel.Ffmpeg/Ffmpeg.cs`.

Ступень сжатия одна на все форматы, а каждый формат переводит её в свои параметры (crf, битрейт, q). Новый формат — это одна строчка `Target` в `Ffmpeg.cs`.

Лицензия: MIT. FFmpeg ставится отдельно и распространяется под своей лицензией.
