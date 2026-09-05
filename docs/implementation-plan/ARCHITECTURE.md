# Архитектура и карта кода

## Запуск и сборки кода

Обычный путь запуска: `ProductBootstrap → ProductEntry → ProductLobby → игровая карта → результаты → лобби`. Сцены находятся в [Production/Scenes](../../Assets/_Game/Production/Scenes), список сборки — в [EditorBuildSettings.asset](../../ProjectSettings/EditorBuildSettings.asset).

| Область | Назначение |
| --- | --- |
| [Scripts](../../Assets/_Game/Scripts) | `HowToSuck.Runtime`: игровая логика, ввод, предметы, бой, UI, сохранения |
| [Networking](../../Assets/_Game/Networking) | `HowToSuck.Networking`: NGO, транспорты EOS/Steam, сессия и репликация |
| [Networking/Editor](../../Assets/_Game/Networking/Editor) | Настройка провайдера и обработчики сборки, только для редактора |
| [Production](../../Assets/_Game/Production) | Продуктовые сцены, префабы и каталоги |
| [Data](../../Assets/_Game/Data) | Определения предметов, пылесосов, противников и других игровых данных |
| [Art](../../Assets/_Game/Art) | Импортированные ресурсы игры |

Runtime отделён от SDK сетевых сервисов. Networking ссылается на Runtime и связывает игровую модель с NGO и выбранным провайдером. Точные зависимости задают соответствующие `.asmdef`.

## Владельцы состояния

| Компонент | Ответственность |
| --- | --- |
| [AuthorityWorld](../../Assets/_Game/Scripts/Core/AuthorityWorld.cs) | Авторитетный шаг симуляции, игроки, предметы, поглощение и бой |
| [PlayerInputReader](../../Assets/_Game/Scripts/Player/PlayerInputReader.cs) | Локальный ввод и намерения игрока |
| [ItemFireService](../../Assets/_Game/Scripts/Items/ItemFireService.cs) | Проверка выстрела, выпуск экземпляра и обработка попаданий |
| [ContractController](../../Assets/_Game/Scripts/Contracts/ContractController.cs) | Цели, фазы, время и итог контракта |
| [SaveRepository](../../Assets/_Game/Scripts/Progression/SaveRepository.cs) | Чтение и запись кампании, резервный файл и блокировка записи |
| [NetworkConnectionCoordinator](../../Assets/_Game/Networking/Runtime/NetworkConnectionCoordinator.cs) | Запуск и завершение локального или онлайн-соединения |

Хост подтверждает изменения имущества, урона, доставки и результата. Клиент передаёт ввод; интерфейс и эффекты отображают подтверждённое состояние. `RunId` отделяет один контракт от другого, а `LootKey` идентифицирует конкретный экземпляр предмета независимо от его физического представления.

Личный приём резервирует предмет и место в хранилище. Выстрел освобождает место и выпускает тот же экземпляр в мир. Только доставка грузовику увеличивает сданную стоимость. Повторный приём, выстрел или сетевое сообщение не должны создавать вторую оплату.

## Сохранения

Кампания, настройки и личные достижения имеют отдельные хранилища:

- [CampaignStoragePaths](../../Assets/_Game/Scripts/Progression/CampaignStoragePaths.cs) задаёт расположение кампании.
- [LocalSettingsRepository](../../Assets/_Game/Scripts/Settings/LocalSettingsRepository.cs) обслуживает настройки.
- [AchievementProfileRepository](../../Assets/_Game/Scripts/Achievements/AchievementProfileRepository.cs) обслуживает достижения.

Гость не записывает кампанию хоста. Рабочие сохранения и локальные настройки не являются исходными ресурсами проекта.

Далее: [контент и баланс](CONTENT.md), [проверка игрового цикла](tasks/21-gameplay-decisions.md), [сеть](../networking/EPIC_SETUP_RU.md), [обновлённый GDD](../How_to_Suck_GDD_updated.md).
