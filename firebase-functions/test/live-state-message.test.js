"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const {buildClubLiveStateData} = require("../live-state-message");

test("missing optional values are not converted to zero", () => {
  const data = buildClubLiveStateData("club_1", {});

  assert.equal(Object.hasOwn(data, "financialPacePercent"), false);
  assert.equal(Object.hasOwn(data, "financialPaceAvailable"), false);
  assert.equal(Object.hasOwn(data, "cashAcceptancePresent"), false);
});

test("an explicit zero pace is delivered as a real value", () => {
  const data = buildClubLiveStateData("club_1", {
    financialPacePercent: 0,
    financialPaceAvailable: true,
  });

  assert.equal(data.financialPacePercent, "0");
  assert.equal(data.financialPaceAvailable, "true");
});

test("provisional cash acceptance is delivered as compact json", () => {
  const data = buildClubLiveStateData("club_1", {
    cashAcceptancePresent: true,
    latestCashAcceptance: {
      isProvisional: true,
      actualAmount: 2901,
      finalizeAtUnixMs: 1788320400000,
    },
  });

  assert.equal(data.cashAcceptancePresent, "true");
  assert.deepEqual(JSON.parse(data.cashAcceptanceJson), {
    isProvisional: true,
    actualAmount: 2901,
    finalizeAtUnixMs: 1788320400000,
  });
});

test("an explicit empty acceptance clears the cached card", () => {
  const data = buildClubLiveStateData("club_1", {
    cashAcceptancePresent: false,
  });

  assert.equal(data.cashAcceptancePresent, "false");
  assert.equal(data.cashAcceptanceJson, "");
});
