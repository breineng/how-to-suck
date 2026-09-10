# Сетевая игра

## Режимы

Игровая симуляция использует Netcode for GameObjects. Хост управляет миром; сессия рассчитана максимум на четыре игрока. Основная точка входа — [NetworkConnectionCoordinator.cs](../../Assets/_Game/Networking/Runtime/NetworkConnectionCoordinator.cs).

- **Solo:** локальный NGO-хост через Unity Transport на loopback. Внешний игровой сервис для запуска не требуется.
- **Epic Online Services:** комнаты и P2P-транспорт EOS, подключение по коду комнаты.
- **Steam:** Steam lobby и транспорт Steam Networking.

Провайдер выбирается вне Play Mode через **How to Suck → Networking → Select Epic Online Services** или **Select Steam**. Выбор сохраняется в [OnlineServicesSettings.asset](../../Assets/_Game/Resources/OnlineServicesSettings.asset). В текущей конфигурации выбран EOS. После смены провайдера пересоберите игру для всех участников.

## Локальная конфигурация EOS

Данные своего игрового клиента EOS сохраните в `.local/eos-client.json` в корне проекта. Используется игровой клиент с ограниченной политикой Peer2Peer, а не административный ключ организации.

Схема файла; обозначения в угловых скобках нужно заменить своими значениями:

```json
{
  "ProductId": "<product-id>",
  "SandboxId": "<sandbox-id>",
  "DeploymentId": "<deployment-id>",
  "ClientId": "<client-id>",
  "ClientSecret": "<game-client-secret>",
  "ForceRelay": false
}
```

Эти обозначения не являются действующей конфигурацией. Все участники используют совместимые сборки и одно окружение EOS. `ForceRelay` передаёт транспорту настройку принудительного ретранслятора.

[EosClientConfiguration.cs](../../Assets/_Game/Networking/Runtime/EosClientConfiguration.cs) читает файл из `.local` в редакторе и из `StreamingAssets` в готовой игре. [EosBuildConfiguration.cs](../../Assets/_Game/Networking/Editor/EosBuildConfiguration.cs) копирует его в `<имя игры>_Data/StreamingAssets/eos-client.json` при Windows x64 сборке с выбранным EOS. Следовательно, этот файл входит в распространяемую сборку; используйте только данные игрового клиента с соответствующими правами. Сам локальный файл исключён из Git.

Если файла нет, сборка поддерживает Solo, а онлайн-комнаты остаются недоступны. Некорректная существующая конфигурация останавливает сборку. Значения ключей не нужно добавлять в сцены, префабы или документацию.

## Steam

Параметры находятся в [SteamEntryConfiguration.asset](../../Assets/_Game/Production/Data/SteamEntryConfiguration.asset). Для собственного приложения предусмотрены режим `Production` и `ProductionAppId`. Диагностический AppID 480 допускается кодом конфигурации для редактора и Development Build и не заменяет идентификатор выпускаемой игры.

Steam-режим требует доступного Steam-клиента и настроенного приложения. Правила проверки находятся в [NetworkConfiguration.cs](../../Assets/_Game/Networking/Pure/NetworkConfiguration.cs) и [SteamEntryConfiguration.cs](../../Assets/_Game/Networking/Runtime/SteamEntryConfiguration.cs).

## Проверка кооператива

На двух компьютерах запустите одинаковую сборку с одинаковым провайдером. Создайте комнату, подключите гостя, проверьте запуск контракта, совместную работу с предметами, доставку босса, результаты и следующий контракт.

Вход во время контракта закрыт; уход хоста завершает сессию без переноса мира на другого игрока. Для EOS используется Device ID: два процесса в одном профиле ОС не заменяют проверку двух отдельных игроков. Проверка соединения через интернет и ретранслятор проводится отдельно от локального Solo.
