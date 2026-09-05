# Запуск и сборка

## Подготовка

Проект использует **Unity 6000.3.6f1**. Точная версия записана в [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt). Для целевой Windows x64 сборки установите соответствующий модуль редактора.

Бинарные ресурсы хранятся в Git LFS. После клонирования выполните в корне проекта:

```shell
git lfs install
git lfs pull
git lfs fsck
```

Затем откройте корень проекта в Unity и дождитесь импорта. Зависимости и их закреплённые версии находятся в [manifest.json](../Packages/manifest.json) и [packages-lock.json](../Packages/packages-lock.json). EOS SDK подключается из [ThirdParty/EosSdk](../ThirdParty/EosSdk); Steamworks.NET — из указанного в манифесте Git-репозитория. Для восстановления пакетов нужен доступ к их источникам.

## Запуск в редакторе

Выберите **How to Suck → Играть**. Команда запускает [ProductBootstrap.unity](../Assets/_Game/Production/Scenes/ProductBootstrap.unity), затем открывается главное меню. Выберите одиночную игру.

При прямом запуске игровой карты отсутствует созданная главным меню сессия. Для обычного игрового цикла используйте ProductBootstrap. Поведение команды находится в [ProductProjectEntry.cs](../Assets/_Game/Editor/ProductProjectEntry.cs).

Одиночный режим использует локальную сетевую симуляцию и доступен без настроенного EOS или Steam. Настройка совместной игры описана в [руководстве по сети](networking/EPIC_SETUP_RU.md).

## Windows x64

Создайте сборку для Windows x64 средствами Unity. Используйте включённые сцены из [EditorBuildSettings.asset](../ProjectSettings/EditorBuildSettings.asset) в сохранённом порядке:

1. ProductBootstrap
2. ProductEntry
3. ProductLobby
4. OldHouseNetwork
5. SupermarketNetwork
6. WarehouseNetwork

Вывод сборки можно разместить в `Builds/`, которая исключена из Git. При выбранном EOS обработчик сборки переносит существующий локальный `eos-client.json` в `StreamingAssets` готовой игры. Подробности и состав конфигурации — в [руководстве по сети](networking/EPIC_SETUP_RU.md).

После изменений проверьте запуск из главного меню, вход в контракт, возврат в лобби и повторный запуск. Для изменений сетевой игры отдельно проверьте одинаковую сборку на двух компьютерах. Это порядок проверки, а не утверждение о прохождении тестов на вашей машине.
