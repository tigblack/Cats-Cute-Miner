# Adjust: Cats Cute Miner

Создано минимальное собственное приложение через Adjust AppView без шаблона. App token: `cdbdt3hkj56o`; Android / Google Play: `com.multicastgames.catscuteminer`; reporting currency: USD. Имя, токен, валюта и сохранённая платформа перечитаны в UI. Поиск по имени и package до создания не дал результатов.

Созданы и прочитаны все 52 точных имени из `events.required.json` плюс отдельное обычное `purchase` (`fv23mx`): 53 различных токена, без лишних событий. Unique выключен; Source/Flow/Revenue checks Off. Параметры сохранённого purchase дополнительно перечитаны через Edit. Purchase Verification не подключалась; callbacks не настраивались. CSV/чужой импорт не использовался.

В своей карточке SuperLeo `e4e954f1-59a9-479a-821f-be7eed9aff47` единственный PATCH записал `adjustTrackerToken = cdbdt3hkj56o` вместо null; повторный GET подтвердил результат. Это SDK app token.

Токены: `release/google-play/adjust-tokens.json`; обезличенное свидетельство: `release/google-play/evidence/adjust-dashboard-current.json`. Историческое свидетельство отказа SuperLeo create без шаблона оставлено без изменения; этот маршрут не повторялся.

Ожидается от родителя: запись токенов в профиль/конфиг, Android build и подтверждение доставки реальных событий/покупок/ILRD в production Adjust. Axon data sharing и mapping purchase остаются родителю после MAX SDK key / 2FA. Регистрация событий сама по себе не подтверждает доставку с устройства.

Mixed audience с детьми не изменялась. Защита under16/unknown и отдельное согласие для optional Adjust только16+ остаются обязательными. `profile.json`, `status.md`, `events.required.json` и `Assets/Game/Resources/Integrations/cats-cute-miner-sdk.json` помощник не менял. Commit/push/смена ветки не выполнялись.
