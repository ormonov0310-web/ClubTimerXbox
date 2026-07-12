const {initializeApp} = require("firebase-admin/app");
const {getDatabase} = require("firebase-admin/database");
const {getMessaging} = require("firebase-admin/messaging");
const {logger, setGlobalOptions} = require("firebase-functions");
const {onValueCreated} = require("firebase-functions/v2/database");

initializeApp();
setGlobalOptions({region: "europe-west1", maxInstances: 2});

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
      const body = String(clubEvent.body || "Новое событие клуба");
      const severity = String(clubEvent.severity || "info");

      const devicesSnapshot = await getDatabase()
          .ref("owner/notificationDevices")
          .get();
      const devices = devicesSnapshot.val() || {};
      const targets = Object.entries(devices)
          .filter(([, device]) => device && device.enabled !== false && device.fid)
          .map(([deviceId, device]) => ({deviceId, fid: String(device.fid)}));

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

      const channelId = severity === "urgent" ? "club_urgent" : "club_events";
      const response = await getMessaging().sendEachForMulticast({
        fids: targets.map((target) => target.fid),
        notification: {title, body},
        data: {
          clubId,
          eventId,
          eventType: String(clubEvent.type || "club_event"),
          severity,
          title,
          body,
        },
        android: {
          priority: severity === "urgent" ? "high" : "normal",
          notification: {
            channelId,
            sound: "default",
          },
        },
      });

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
