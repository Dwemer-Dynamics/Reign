import assert from "node:assert/strict";
import fs from "node:fs";
import { getSampleDataForPrefab } from "./js/sample-data.js";

const catalog = JSON.parse(fs.readFileSync(new URL("./calibration/ui-catalog.json", import.meta.url), "utf8"));
const court = catalog.interfaces.find((entry) => entry.id === "court-petition");
assert.ok(court.alternateViewModels.includes("ReignCourtLifeScreenVM"));
const index = fs.readFileSync(new URL("./index.html", import.meta.url), "utf8");
for (const suffix of ["international", "family", "patronage", "visitors"]) {
  const id = `court-life-${suffix}`;
  const state = court.previewStates.find((entry) => entry.id === id);
  assert.ok(state?.previewOnly && state.nativeInjection === false);
  assert.ok(index.includes(`data-interface="${id}"`) && index.includes(`data-preset="${id}"`));
  const data = getSampleDataForPrefab("ReignCourtPetitionScreen.xml", id);
  assert.ok(data.ActiveParticipants.length >= 1 && data.ActiveParticipants.length <= 4);
  assert.equal(data.AvailableAttendees.length, 0);
  assert.equal(data.DirectGrantLabel, "REVIEW DECISIONS");
  assert.equal(data.GoldGrantVisible, true);
  assert.ok(data.Transcript.every((line) => !/\+3|\+5|dismissal|jealousy trait/.test(line.Text)));
  assert.doesNotMatch(JSON.stringify(data), /Guildmaster Eronys|seed grain/);
}
assert.equal(getSampleDataForPrefab("ReignCourtPetitionScreen.xml", "court-life-visitors").ActiveParticipants.length, 4);
for (const id of ["court-life-family", "court-life-patronage"])
  assert.equal(getSampleDataForPrefab("ReignCourtPetitionScreen.xml", id).ActiveParticipants[0].HasPortrait, false);
console.log("Court-life preview source and fixed-participant state contracts: PASS");
