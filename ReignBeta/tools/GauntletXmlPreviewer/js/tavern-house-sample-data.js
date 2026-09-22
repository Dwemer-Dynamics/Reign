import { previewActionRichText } from "./action-text.js";

// Existing unchanged sources make every preview independent of generation providers.
const source = (folder) => `../../PortraitCache/_shared/${folder}/source.png`;
const portraits = [source("Ira (lord_1_37)"), source("Corein (lord_5_10)"),
  source("Abagai (lord_6_12)"), source("Adalindis (lord_4_28_1)"),
  source("Aeron (lord_5_16)"), source("Mengus (lord_5_7)")];
const names = ["Livia Bellori", "Beatrice Voss", "Zara Caldera", "Hilda Rainault", "Oren Mercato", "Tobias Marin"];

function member(index, missing = false) {
  return {
    Name: names[index], Status: missing ? "Use the eye for a portrait" : index === 0 ? "Madam" : "House worker",
    DisplayStatus: index === 0 ? "Madam" : "House worker", IsActive: false, IsInactive: true,
    CanVisitInPerson: false, HasGeneratedPortrait: !missing,
    PortraitId: missing ? "" : `tavern_house_preview_${index}_portrait`,
    PortraitAsset: missing ? "" : portraits[index], PortraitAdditionalArgs: "",
    PortraitTextureProviderName: "ReignPortraitProvider", PortraitCacheKey: `tavern_house_preview_${index}`,
    PortraitCropImageWidth: 168, PortraitCropImageHeight: 126,
    StatusTop: 65, StatusHeight: 28
  };
}

function line(Speaker, Text, npc = true) {
  return { Speaker, Text, RichText: previewActionRichText(Text, npc), Role: npc ? "npc" : "player",
    IsNpcLine: npc, IsPlayerLine: !npc, IsSystemLine: false };
}

export const TAVERN_HOUSE_STATES = ["negotiating", "quote", "payment-pending", "paid", "image-pending",
  "image-failed", "images-disabled", "portraits-missing", "max-workers-long-chat", "portrait-preview", "recruitment-offer"];

export function getTavernHouseSampleData(preset = "") {
  const state = String(preset || "negotiating").replace(/^tavern-house-/, "");
  if (!TAVERN_HOUSE_STATES.includes(state)) throw new Error(`Unknown tavern house preview state: ${preset}`);
  const visiting = ["paid", "image-pending", "image-failed", "images-disabled", "max-workers-long-chat"].includes(state);
  const imageReady = ["paid", "image-pending", "max-workers-long-chat"].includes(state);
  const recruiting = state === "recruitment-offer";
  const quote = state === "quote" || recruiting;
  const waiting = state === "payment-pending";
  const result = {
    IsOverlayVisible: true, Title: "Visit the Madam", TownName: "Zeonica", IsBusy: false,
    Madam: [member(0, state === "portraits-missing")],
    Workers: [1, 2, 3, 4, 5].map(index => member(index, state === "portraits-missing")),
    IsVisiting: visiting, HasQuote: quote, PaymentConfirmationPending: waiting, CanAccept: quote || waiting, CanNegotiate: quote,
    InputEnabled: !waiting, CanSend: !waiting, CanLookAgain: visiting && state !== "image-pending" && state !== "images-disabled",
    ImagesEnabled: state !== "images-disabled", ImageToggleText: state === "images-disabled" ? "Scene images: OFF" : "Scene images: ON",
    HasSceneImage: imageReady, ShowScenePlaceholder: !imageReady,
    SceneImageId: imageReady ? "tavern_house_preview_scene" : "",
    EventImageAsset: imageReady ? "../../EventArt/feast_empire/toasts_and_table_talk.png" : "",
    ImageStatus: state === "image-pending" ? "Creating the next scene…"
      : state === "image-failed" ? "The scene could not be created. Look Again to retry."
      : state === "images-disabled" ? "Scene images are off."
      : imageReady ? "" : "A scene will appear after an agreement is accepted.",
    StatusText: waiting ? "Payment is complete. Retry confirmation to continue without paying again."
      : quote ? "Review the names and total before accepting." : "",
    SendText: "SEND", AcceptText: recruiting ? "ACCEPT RECRUITMENT" : waiting ? "RETRY CONFIRMATION" : "ACCEPT AND PAY",
    QuoteText: recruiting ? "Oren Mercato agrees to join your party.\nAgreed recruitment payment: 500 denars."
      : quote ? "Beatrice Voss — 140 denars\nOren Mercato — 160 denars\nTotal: 300 denars" : "",
    VisitSummary: visiting ? "Private visit · Beatrice Voss, Zara Caldera, Oren Mercato, Tobias Marin"
      : waiting ? "Payment complete · confirmation pending" : "Speak with the madam to agree on company and payment.",
    InputText: "Tell me more about your house.", ChatScrollVersion: 2,
    ChatLines: visiting ? [line("Beatrice Voss", "*She draws a chair close to the table.* We have time to talk. What brought you to Zeonica?"),
      line("You", "The road has been long, and I would welcome some company.", false)]
      : [line("Livia Bellori", "Welcome. Tell me whose company interests you, and we can discuss the terms."),
        line("You", "I would like to hear about the house and its people.", false)],
    IsPortraitPreviewVisible: state === "portrait-preview", PortraitPreviewName: names[0],
    PortraitPreviewId: "tavern_house_preview_0_portrait", PortraitPreviewAsset: portraits[0],
    PortraitPreviewAdditionalArgs: "", PortraitPreviewTextureProviderName: "ReignPortraitProvider",
    PortraitPreviewCacheKey: "tavern_house_preview_0"
  };
  result.HasImageStatus = Boolean(result.ImageStatus);
  if (visiting) result.Workers.forEach((person, index) => {
    person.IsActive = index !== 2; person.IsInactive = !person.IsActive;
    person.Status = person.IsActive ? "Visiting with you" : "House worker";
  });
  if (state === "max-workers-long-chat") {
    result.Workers[2].Name = "Hilda Rainault of the Western Market";
    result.ChatLines = Array.from({ length: 30 }, (_, index) => line(index % 2 ? "You" : names[index % 5 + 1],
      index % 2 ? "I remember the journey through the western pass. The roads were quiet, but every inn had a different story to tell."
        : "*A quiet smile accompanies the reply.* There are stories enough for a long evening, from the busy market to the travelers who arrive with each caravan. Tell us which part of the journey you remember most.", index % 2 === 0));
    result.ChatScrollVersion = result.ChatLines.length;
  }
  return result;
}

export const TAVERN_HOUSE_ROOT_KEYS = Object.keys(getTavernHouseSampleData());
