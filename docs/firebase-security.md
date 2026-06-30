# Firebase security rollout

Цель: закрыть Realtime Database от публичного чтения и записи, но не сломать ПК и Android-приложение.

## Порядок включения

1. В Firebase Console включить Authentication -> Sign-in method -> Email/Password.
2. Создать пользователей для владельца и клубных ПК.
3. Взять Web API Key проекта и вставить его в:
   - `ClubTimerXbox/Services/FirebaseSettings.cs`
   - `GGGsel/android-owner-app/app/src/main/java/com/clubtimer/owner/MainActivity.kt`
4. Собрать и установить обновлённые ПК и Android-приложения.
5. Войти в Firebase на каждом устройстве хотя бы один раз.
6. Добавить UID созданных пользователей в базу:

```json
{
  "security": {
    "allowedUids": {
      "UID_ВЛАДЕЛЬЦА": true,
      "UID_ПК_КЛУБ_1": true,
      "UID_ПК_КЛУБ_2": true
    }
  }
}
```

7. Только после этого применить `firebase-database.rules.json`.

## Почему не просто auth != null

Правило `auth != null` разрешает доступ любому, кто сумел войти в этот Firebase-проект. Для нашего проекта безопаснее держать allow-list по `auth.uid`, чтобы доступ был только у заранее разрешённых пользователей.

## Что уже подготовлено в коде

- ПК-приложение умеет получать Firebase ID token через Email/Password и добавлять его к REST-запросам.
- Android-приложение умеет делать то же самое.
- Если Web API Key пустой, приложения временно работают по старой схеме, чтобы не сломать текущую установку.
- Правила базы подготовлены, но не включены автоматически.
