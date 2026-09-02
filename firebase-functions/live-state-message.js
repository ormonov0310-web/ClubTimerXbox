"use strict";

function hasOwn(value, name) {
  return Object.prototype.hasOwnProperty.call(value, name);
}

function stringValue(source, name, fallback = "") {
  return String(source[name] === undefined ? fallback : source[name]);
}

function buildClubLiveStateData(clubId, liveState) {
  const data = {
    messageType: "club_state",
    signalType: stringValue(liveState, "signalType", "heartbeat"),
    clubId,
    clubName: stringValue(liveState, "clubName", "Club Timer"),
    revision: stringValue(liveState, "revision", Date.now()),
    updatedAtUnixMs: stringValue(liveState, "updatedAtUnixMs", Date.now()),
    lastHeartbeatAtUnixMs: stringValue(
        liveState,
        "lastHeartbeatAtUnixMs",
        Date.now(),
    ),
    isOpen: stringValue(liveState, "isOpen", true),
    connectionState: stringValue(liveState, "connectionState", "online"),
    employeeName: stringValue(liveState, "employeeName"),
    busyPlaces: stringValue(liveState, "busyPlaces", 0),
    freePlaces: stringValue(liveState, "freePlaces", 0),
    gamesToday: stringValue(liveState, "gamesToday", 0),
    acceptanceRequired: stringValue(
        liveState,
        "acceptanceRequired",
        false,
    ),
    acceptanceCompleted: stringValue(
        liveState,
        "acceptanceCompleted",
        false,
    ),
  };

  // Optional values stay optional so an older PC cannot erase a newer cache.
  if (hasOwn(liveState, "financialPacePercent")) {
    data.financialPacePercent = stringValue(
        liveState,
        "financialPacePercent",
        0,
    );
  }
  if (hasOwn(liveState, "financialPaceAvailable")) {
    data.financialPaceAvailable = stringValue(
        liveState,
        "financialPaceAvailable",
        false,
    );
  }

  if (hasOwn(liveState, "cashAcceptancePresent")) {
    const declaredPresent = liveState.cashAcceptancePresent === true ||
      String(liveState.cashAcceptancePresent).toLowerCase() === "true";
    const cashAcceptance = liveState.latestCashAcceptance;
    const isPresent = Boolean(
        declaredPresent &&
        cashAcceptance &&
        typeof cashAcceptance === "object" &&
        !Array.isArray(cashAcceptance),
    );
    if (!declaredPresent || isPresent) {
      data.cashAcceptancePresent = String(isPresent);
      data.cashAcceptanceJson = isPresent ? JSON.stringify(cashAcceptance) : "";
    }
  } else if (hasOwn(liveState, "latestCashAcceptance")) {
    const cashAcceptance = liveState.latestCashAcceptance;
    const isPresent = Boolean(
        cashAcceptance &&
        typeof cashAcceptance === "object" &&
        !Array.isArray(cashAcceptance),
    );
    data.cashAcceptancePresent = String(isPresent);
    data.cashAcceptanceJson = isPresent ? JSON.stringify(cashAcceptance) : "";
  }

  return data;
}

module.exports = {buildClubLiveStateData};
