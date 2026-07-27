const {initializeApp} = require("firebase-admin/app");
const {getDatabase} = require("firebase-admin/database");
const {getMessaging} = require("firebase-admin/messaging");
const {logger, setGlobalOptions} = require("firebase-functions");
const {onValueCreated, onValueWritten} =
  require("firebase-functions/v2/database");
const {onSchedule} = require("firebase-functions/v2/scheduler");

initializeApp();
setGlobalOptions({region: "europe-west1", maxInstances: 2});

const CONNECTION_LOST_AFTER_MS = 3 * 60 * 1000;
const MAX_CLUB_EVENT_AGE_MS = 60 * 60 * 1000;

async function getNotificationTargets() {
  const devicesSnapshot = await getDatabase()
      .ref("owner/notificationDevices")
      .get();
  const devices = devicesSnapshot.val() || {};
  return Object.entries(devices)
      .filter(([, device]) => device && device.enabled !== false && device.fid)
      .map(([deviceId, device]) => ({deviceId, fid: String(device.fid)}));
}

async function disableInvalidTargets(targets, response) {
  const deviceUpdates = {};
  response.responses.forEach((sendResponse, index) => {
    if (sendResponse.success) return;

    const errorCode = sendResponse.error && sendResponse.error.code;
    if (errorCode === "messaging/installation-id-not-registered" ||
        errorCode === "messaging/registration-token-not-registered" ||
        errorCode === "messaging/invalid-registration-token") {
      const deviceId = targets[index].deviceId;
      deviceUpdates[`owner/notificationDevices/${deviceId}/enabled`] = false;
      deviceUpdates[`owner/notificationDevices/${deviceId}/disabledAt`] =
        new Date().toISOString();
    }
  });

  if (Object.keys(deviceUpdates).length > 0) {
    await getDatabase().ref().update(deviceUpdates);
  }
}

function eventTime(clubEvent) {
  const localTime = String(clubEvent.createdAtLocal || "")
      .match(/(\d{1,2}:\d{2})(?::\d{2})?$/);
  if (localTime) return localTime[1];

  const bodyTime = String(clubEvent.body || "")
      .match(/\b(\d{1,2}:\d{2})\b/);
  return bodyTime ? bodyTime[1] : "--:--";
}

function employeeName(value) {
  return String(value || "").trim() || "Не выбран";
}

function amount(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.trunc(parsed) : 0;
}

function formatCashDifference(value) {
  const difference = amount(value);
  if (difference < 0) {
    return `Недостача налички: ${Math.abs(difference)} сом`;
  }
  if (difference > 0) {
    return `Излишек налички: ${difference} сом`;
  }
  return "Наличка: без расхождений";
}

function formatProductDifferences(shortageValue, extraValue) {
  const shortage = Math.max(0, amount(shortageValue));
  const extra = Math.max(0, amount(extraValue));
  const lines = [];
  if (shortage > 0) {
    lines.push(`Недостача товаров: ${shortage} сом`);
  }
  if (extra > 0) {
    lines.push(`Излишек товаров: ${extra} сом`);
  }
  return lines.length > 0 ? lines : ["Товары: без расхождений"];
}

function formatClubEventBody(clubEvent) {
  const type = String(clubEvent.type || "");
  const employee = employeeName(clubEvent.employeeName);
  const previous = employeeName(clubEvent.previousEmployeeName);
  const current = employeeName(
      clubEvent.currentEmployeeName || clubEvent.employeeName,
  );

  if (type === "club_opened") {
    return [
      `Клуб открыт: ${eventTime(clubEvent)}`,
      `Сотрудник: ${employee}`,
    ].join("\n");
  }
  if (type === "club_closed") {
    return [
      `Клуб закрыт: ${eventTime(clubEvent)}`,
      `Сотрудник: ${employee}`,
    ].join("\n");
  }
  if (type === "employee_changed") {
    return [
      `${previous} → ${current}`,
      `Время смены: ${eventTime(clubEvent)}`,
    ].join("\n");
  }
  if (type === "acceptance_completed") {
    return [
      `${previous} → ${current}`,
      formatCashDifference(clubEvent.cashDifference),
      ...formatProductDifferences(
          clubEvent.productShortageAmount,
          clubEvent.productExtraAmount,
      ),
    ].slice(0, 4).join("\n");
  }
  return String(clubEvent.body || "Новое событие клуба");
}

function minuteWord(value) {
  const lastTwo = value % 100;
  if (lastTwo >= 11 && lastTwo <= 14) return "минут";
  const last = value % 10;
  if (last === 1) return "минута";
  if (last >= 2 && last <= 4) return "минуты";
  return "минут";
}

async function sendOwnerNotification({
  targets,
  clubId,
  title,
  body,
  eventId,
  eventType,
  severity,
  extraData = {},
}) {
  if (targets.length === 0) {
    logger.warn("No enabled owner notification devices", {clubId, eventType});
    return {successCount: 0, failureCount: 0};
  }

  const message = {
    fids: targets.map((target) => target.fid),
    data: {
      clubId,
      eventId,
      eventType,
      severity,
      title,
      body,
      ...extraData,
    },
    android: {
      priority: "high",
    },
  };
  const response = await getMessaging().sendEachForMulticast(message);

  await disableInvalidTargets(targets, response);
  return response;
}

exports.sendClubEventNotification = onValueCreated(
    {
      ref: "/clubs/{clubId}/events/{eventId}",
      instance: "clubtimerxbox-default-rtdb",
    },
    async (event) => {
      const clubEvent = event.data.val() || {};
      const clubId = event.params.clubId;
      const eventId = event.params.eventId;
      const title = String(clubEvent.clubName || clubEvent.title || "Club Timer");
      const body = formatClubEventBody(clubEvent);
      const severity = String(clubEvent.severity || "info");
      const eventCreatedAt = Date.parse(event.time || "");

      if (Number.isFinite(eventCreatedAt) &&
          Date.now() - eventCreatedAt > MAX_CLUB_EVENT_AGE_MS) {
        await event.data.ref.child("delivery").set({
          completedAt: new Date().toISOString(),
          successCount: 0,
          failureCount: 0,
          state: "skipped_stale",
        });
        logger.info("Stale club event skipped", {
          clubId,
          eventId,
          eventCreatedAt,
        });
        return;
      }

      const targets = await getNotificationTargets();

      if (targets.length === 0) {
        await event.data.ref.child("delivery").set({
          completedAt: new Date().toISOString(),
          successCount: 0,
          failureCount: 0,
          state: "no_devices",
        });
        logger.warn("No enabled owner notification devices", {clubId, eventId});
        return;
      }

      const response = await sendOwnerNotification({
        targets,
        clubId,
        title,
        body,
        eventId,
        eventType: String(clubEvent.type || "club_event"),
        severity,
      });

      await event.data.ref.child("delivery").set({
        completedAt: new Date().toISOString(),
        successCount: response.successCount,
        failureCount: response.failureCount,
        state: response.failureCount === 0 ? "delivered" : "partial",
      });

      logger.info("Club event notification sent", {
        clubId,
        eventId,
        successCount: response.successCount,
        failureCount: response.failureCount,
      });
    },
);

exports.sendClubLiveState = onValueWritten(
    {
      ref: "/clubs/{clubId}/liveState",
      instance: "clubtimerxbox-default-rtdb",
    },
    async (event) => {
      const liveState = event.data.after.val();
      if (!liveState) return;

      const clubId = event.params.clubId;
      const targets = await getNotificationTargets();

      if (targets.length === 0) {
        logger.warn("No enabled devices for club live state", {clubId});
        return;
      }

      const value = (name, fallback = "") =>
        String(liveState[name] === undefined ? fallback : liveState[name]);
      const response = await getMessaging().sendEachForMulticast({
        fids: targets.map((target) => target.fid),
        data: {
          messageType: "club_state",
          signalType: value("signalType", "heartbeat"),
          clubId,
          clubName: value("clubName", "Club Timer"),
          revision: value("revision", Date.now()),
          updatedAtUnixMs: value("updatedAtUnixMs", Date.now()),
          lastHeartbeatAtUnixMs: value("lastHeartbeatAtUnixMs", Date.now()),
          isOpen: value("isOpen", true),
          connectionState: value("connectionState", "online"),
          employeeName: value("employeeName"),
          busyPlaces: value("busyPlaces", 0),
          freePlaces: value("freePlaces", 0),
          gamesToday: value("gamesToday", 0),
          acceptanceRequired: value("acceptanceRequired", false),
          acceptanceCompleted: value("acceptanceCompleted", false),
        },
        android: {
          priority: "normal",
          ttl: 180000,
          collapseKey: `club_state_${clubId}`,
        },
      });

      await disableInvalidTargets(targets, response);

      logger.info("Club live state sent", {
        clubId,
        signalType: value("signalType", "heartbeat"),
        successCount: response.successCount,
        failureCount: response.failureCount,
      });
    },
);

exports.checkClubConnections = onSchedule(
    {
      schedule: "every 1 minutes",
      timeZone: "Asia/Bishkek",
      retryCount: 0,
    },
    async () => {
      const database = getDatabase();
      const [ownerClubsSnapshot, alertsSnapshot] = await Promise.all([
        database.ref("owner/clubs").get(),
        database.ref("owner/connectionAlerts").get(),
      ]);
      const ownerClubs = ownerClubsSnapshot.val() || {};
      const alerts = alertsSnapshot.val() || {};
      const targets = await getNotificationTargets();
      const now = Date.now();
      const updates = {};
      const clubIds = Object.keys(ownerClubs);
      const liveStateSnapshots = await Promise.all(
          clubIds.map((clubId) =>
            database.ref(`clubs/${clubId}/liveState`).get(),
          ),
      );

      for (let index = 0; index < clubIds.length; index += 1) {
        const clubId = clubIds[index];
        const ownerClub = ownerClubs[clubId] || {};
        const liveState = liveStateSnapshots[index].val();
        if (!liveState) continue;

        const clubName = String(
            liveState.clubName ||
            ownerClub.name ||
            clubId,
        );
        const isOpen = liveState.isOpen === true;
        const heartbeatAt = Number(
            liveState.lastHeartbeatAtUnixMs ||
            liveState.updatedAtUnixMs ||
            0,
        );
        const previous = alerts[clubId] || {};
        const isLost = isOpen &&
          heartbeatAt > 0 &&
          now - heartbeatAt > CONNECTION_LOST_AFTER_MS;

        if (isLost && previous.offline !== true) {
          const eventId = `${clubId}_connection_lost_${heartbeatAt}`;
          const offlineMinutes = Math.max(
              3,
              Math.floor((now - heartbeatAt) / 60000),
          );
          const body = [
            "Связь с ПК потеряна",
            `Нет связи: ${offlineMinutes} ${minuteWord(offlineMinutes)}`,
          ].join("\n");
          const response = await sendOwnerNotification({
            targets,
            clubId,
            title: clubName,
            body,
            eventId,
            eventType: "connection_lost",
            severity: "urgent",
            extraData: {
              messageType: "connection_state",
              connectionState: "offline",
              signalType: "connection_lost",
              clubName,
              isOpen: "true",
              lastHeartbeatAtUnixMs: String(heartbeatAt),
              stateUpdatedAtUnixMs: String(now),
            },
          });
          updates[`owner/connectionAlerts/${clubId}`] = {
            offline: true,
            lostAtUnixMs: now,
            lastHeartbeatAtUnixMs: heartbeatAt,
            notificationSuccessCount: response.successCount,
            notificationFailureCount: response.failureCount,
          };
          logger.warn("Club connection lost", {clubId, clubName, heartbeatAt});
          continue;
        }

        if (!isLost && previous.offline === true) {
          if (!isOpen) {
            updates[`owner/connectionAlerts/${clubId}`] = {
              offline: false,
              closedAtUnixMs: now,
              lastHeartbeatAtUnixMs: heartbeatAt,
            };
            logger.info("Connection alert cleared for closed club", {
              clubId,
              clubName,
            });
            continue;
          }

          const eventId = `${clubId}_connection_restored_${heartbeatAt}`;
          const response = await sendOwnerNotification({
            targets,
            clubId,
            title: clubName,
            body: "Связь с ПК восстановлена.",
            eventId,
            eventType: "connection_restored",
            severity: "info",
            extraData: {
              messageType: "connection_state",
              connectionState: "online",
              signalType: "connection_restored",
              clubName,
              isOpen: "true",
              lastHeartbeatAtUnixMs: String(heartbeatAt),
              stateUpdatedAtUnixMs: String(now),
            },
          });
          updates[`owner/connectionAlerts/${clubId}`] = {
            offline: false,
            recoveredAtUnixMs: now,
            lastHeartbeatAtUnixMs: heartbeatAt,
            notificationSuccessCount: response.successCount,
            notificationFailureCount: response.failureCount,
          };
          logger.info("Club connection restored", {clubId, clubName});
        }
      }

      if (Object.keys(updates).length > 0) {
        await database.ref().update(updates);
      }
    },
);
