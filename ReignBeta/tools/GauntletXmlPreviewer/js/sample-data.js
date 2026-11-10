import { getGovernmentHearingSampleData, GOVERNMENT_ROOT_KEYS } from "./government-hearing-sample-data.js";
import { getClanAccordsSampleData, CLAN_ACCORDS_ROOT_KEYS } from "./clan-accords-sample-data.js";
import { getTavernHouseSampleData as getTavernHouseBaseData, TAVERN_HOUSE_ROOT_KEYS } from "./tavern-house-sample-data.js";
import { previewActionRichText } from "./action-text.js";
// The shipped inventory contains these originals and their chest derivatives.
// The renderer selects the chest image only for headshots; zoom/full-body views
// keep this original. Contracts verify both hydrated files against the inventory.
const portrait = (folder) => `../../PortraitCache/_shared/${folder}/portrait.png`;

export const ASSETS = {
  mengus: portrait("Mengus (lord_5_7)"),
  ira: portrait("Ira (lord_1_37)"),
  corein: portrait("Corein (lord_5_10)"),
  caladog: portrait("Caladog (lord_5_1)"),
  rhagaea: portrait("Rhagaea (lord_1_14)"),
  derthert: portrait("Derthert (lord_4_1)"),
  abagai: portrait("Abagai (lord_6_12)"),
  spymasterFullBody: "assets/spymaster-full-body-preview.png",
  aeron: portrait("Aeron (lord_5_16)"),
  adalindis: portrait("Adalindis (lord_4_28_1)"),
  lucon: portrait("Lucon (lord_1_1)"),
  eventArt: "../../EventArt/feast_empire/toasts_and_table_talk.png",
  castleMapEmpire: "../../GUI/SpriteParts/ui_reignbeta_castle_layout/reign_castle_map_empire.png",
  wildernessForest: "../../EventArt/generated_wilderness/forest/generic.png",
  wildernessBackground: "../../GUI/SpriteParts/ui_reignbeta_event/reign_wilderness_event_background.png",
  courtBackground: "../../GUI/SpriteParts/ui_reignbeta_court/reign_court_throne_background.png",
  rulerPetitionCourt: "../../GUI/UiCalibration/reference-scenes/ruler-petition-throne-viewpoint-vlandia.png",
  courtPlayerBanner: "assets/court-player-banner.svg",
  individualChatBackground: "../../GUI/SpriteParts/ui_reignbeta_individual/reign_individual_chat_background.png",
  partyChatBackground: "../../GUI/SpriteParts/ui_reignbeta_party/reign_party_chat_background.png"
};

const portraitFields = (id, asset, status = "Available") => ({
  PortraitId: id,
  PortraitAsset: asset,
  PortraitAdditionalArgs: "",
  PortraitTextureProviderName: "ReignPortraitProvider",
  PortraitCacheKey: `preview_${id}`,
  HasGeneratedPortrait: Boolean(asset),
  PortraitCropImageWidth: 168,
  PortraitCropImageHeight: 126,
  Status: status
});

const correspondencePortraitFields = (id, asset, status) =>
  portraitFields(id, asset.replace(/\/source\.png$/, "/thumbnail.png"), status);

const emptyCastleSlots = (count) => Array.from({ length: count }, () => ({
  PortraitCacheKey: "",
  HasPortrait: false,
  CanOpen: false
}));

const INDIVIDUAL_CHAT_LINES = [
  {
    Speaker: "Conversation",
    Text: "Private conversation with Mengus fen Gruffendoc.",
    IsPlayerLine: false,
    IsNpcLine: false,
    IsSystemLine: true
  },
  {
    Speaker: "Mengus fen Gruffendoc",
    Text: "You asked what the western clans expect from this gathering. They expect proof that your promises will survive the winter, not merely another speech beneath royal banners.",
    IsPlayerLine: false,
    IsNpcLine: true,
    IsSystemLine: false
  },
  {
    Speaker: "Aeric fen Seanel",
    Text: "Then tell them the grain escorts leave within seven days, and that I will ride with the first column myself.",
    IsPlayerLine: true,
    IsNpcLine: false,
    IsSystemLine: false
  },
  {
    Speaker: "Mengus fen Gruffendoc",
    Text: "That answer may satisfy them. Caladog will still demand names, numbers, and the road each company is sworn to defend.",
    IsPlayerLine: false,
    IsNpcLine: true,
    IsSystemLine: false
  }
];

export const SAMPLE_DATA = {
  "ReignActionBuilder.xml": {
    Title: "Begin your sentence", Sentence: "Your action sentence will appear here.", Error: "", Search: "", Entry: "100",
    EntryVisible: false, SearchVisible: false, CanAccept: false, CanBack: false,
    Choices: [
      { Label: "I would like you…", Detail: "", Enabled: true },
      { Label: "I wish to…", Detail: "", Enabled: true }
    ]
  },
  Title: "A Pact Proclaimed Before the Lords of Calradia",
  DateText: "Summer 3, 1088",
  LocationText: "Location: the imperial hall at Amitatys",
  SelectedSummary: "3 characters selected · 2 active · 7 available",
  Description: "A reusable stress-test description long enough to reveal narrow columns, unexpected wrapping, and clipped copy.",

  IsOverlayVisible: true,
  ShowPhaseControls: true,
  ApproachLabel: "Wait Approach",
  NextPhaseLabel: "Next Phase",
  LeaveEventLabel: "End Event",
  ShowWildernessArt: false,
  EventTitle: "Artisan Salon",
  PhaseTitle: "Browsing and Bargaining",
  PhaseStatus: "Phase 3 of 5 | Attendees 12",
  PhaseDescription: "Guests browse, bargain, compliment, and quietly compete through patronage.",
  EventImageId: "feast_empire_toasts_and_table_talk",
  EventImageAsset: ASSETS.eventArt,
  EventImageText: "A Feast Beneath Uneasy Banners\nToasts and Table Talk",
  EventArtSize: 876,
  EventArtRowHeight: 896,
  AttendeeListTopMargin: 48,
  AttendeeListBottomMargin: 74,
  AttendeeTitleTopMargin: 12,
  EventArtTopMargin: 48,
  EventBackgroundImageId: "event_background",
  BackgroundImageId: "event_background",
  BackgroundAsset: ASSETS.eventArt,
  BackgroundAdditionalArgs: "",
  BackgroundTextureProviderName: "ReignEventArtProvider",
  CultureTint: "#6B431B2A",

  PlayerName: "Aeric fen Seanel",
  PlayerSubtitle: "Clan Seanel · sworn to Battania",
  PlayerInfo: "Location: Marunath",
  PlayerPortraitId: "player_portrait",
  PlayerPortraitAsset: ASSETS.aeron,
  PlayerPortraitAdditionalArgs: "",
  PlayerPortraitTextureProviderName: "ReignPortraitProvider",
  PlayerPortraitCropImageWidth: 168,
  PlayerPortraitCropImageHeight: 220,
  PlayerPortraitCacheKey: "preview_player_portrait",
  CurrentLocation: "Main Hall",
  SelectedLocation: "",
  StatusText: "Select your portrait, then choose a room.",
  PlayerTokenArmed: false,
  CanMove: false,
  MoveButtonText: "MOVE TO LOCATION",
  EmptySlots: [{}, {}, {}, {}],
  CastleGardenSlots: emptyCastleSlots(4),
  TrainingYardSlots: emptyCastleSlots(4),
  StableCourtyardSlots: emptyCastleSlots(4),
  GuestBedroomSlotsTop: emptyCastleSlots(8),
  GuestBedroomSlotsBottom: emptyCastleSlots(8),
  NobleSolarSlots: emptyCastleSlots(4),
  InnerCourtyardSlots: emptyCastleSlots(3),
  MainHallSlots: emptyCastleSlots(3),
  RoyalBedroomSlots: emptyCastleSlots(3),
  ThroneRoomSlots: emptyCastleSlots(4),
  LibrarySlots: emptyCastleSlots(3),
  DiningChamberSlots: emptyCastleSlots(3),
  PortraitGallerySlots: emptyCastleSlots(4),
  ChapelSlots: emptyCastleSlots(3),
  BathsSlots: emptyCastleSlots(3),
  BattlementSlots: emptyCastleSlots(4),

  NpcName: "Mengus fen Gruffendoc",
  NpcSubtitle: "Battanian lord · House Gruffendoc",
  NpcInfo: "Location: Marunath",
  NpcPortraitId: "mengus_portrait",
  NpcPortraitAsset: ASSETS.mengus,
  NpcPortraitAdditionalArgs: "",
  NpcPortraitTextureProviderName: "ReignPortraitProvider",
  NpcPortraitCropImageWidth: 168,
  NpcPortraitCropImageHeight: 220,

  IsBusy: false,
  BusyText: "Waiting for Bannerlord Reign...",
  InputEnabled: true,
  ActionComposerId: "preview-action-composer",
  InputText: "",
  SendButtonText: "Send",
  ChatScrollVersion: 7,
  IsPortraitZoomVisible: false,
  IsPregnancyWarningVisible: false,
  CanChangeOutfit: true,
  IsOutfitVisible: false,
  IsOutfitInputVisible: false,
  IsOutfitComparisonVisible: false,
  IsOutfitReady: false,
  IsOutfitChanging: false,
  CanCancelOutfit: true,
  OutfitDescription: "Deep blue linen tunic with silver buttons, ivory cuffs and a narrow brown leather belt.",
  OutfitStatus: "",
  OutfitBeforePath: ASSETS.spymasterFullBody,
  OutfitAfterPath: "",
  OutfitBeforeWidth: 438,
  OutfitBeforeHeight: 584,
  OutfitAfterWidth: 438,
  OutfitAfterHeight: 584,
  IsPortraitPreviewVisible: false,
  ZoomFrameWidth: 620,
  ZoomFrameHeight: 720,
  ZoomPortraitImageWidth: 560,
  ZoomPortraitImageHeight: 680,
  ZoomPortraitId: "zoom_portrait",
  ZoomPortraitAsset: ASSETS.mengus,
  ZoomPortraitAdditionalArgs: "",
  ZoomPortraitTextureProviderName: "ReignPortraitProvider",
  PortraitPreviewFrameWidth: 620,
  PortraitPreviewFrameHeight: 720,
  PortraitPreviewImageWidth: 560,
  PortraitPreviewImageHeight: 680,
  PortraitPreviewId: "preview_portrait",
  PortraitPreviewAsset: ASSETS.corein,
  PortraitPreviewAdditionalArgs: "",
  PortraitPreviewTextureProviderName: "ReignPortraitProvider",
  PortraitPreviewName: "Corein of the fen Gruffendoc",
  PortraitCropImageWidth: 168,
  PortraitCropImageHeight: 220,

  AuthorityText: "ROYAL COURT",
  CourtTitle: "THE ROYAL COURT OF VLANDIA",
  PlayerClan: "Clan dey Meroc",
  PlayerBannerId: "player_clan_banner",
  PlayerBannerAsset: ASSETS.courtPlayerBanner,
  PlayerBannerArgs: "",
  PlayerBannerProvider: "BannerTableauTextureProvider",
  NextDocketText: "Next docket: 8:00 AM",
  UseLargeCourtLayout: false,
  CourtWorkspaceTopInset: 150,
  LargeHomeVisible: false,
  ReferenceHomeVisible: true,
  HomeVisible: true,
  TabContentVisible: false,
  DetailVisible: false,
  AudienceVisible: false,
  ActionsVisible: false,
  ScreenTitle: "COURT",
  ScreenSubtitle: "THE ROYAL COURT AT PRAVEND",
  TimeText: "10:42 AM",
  StatusText: "Court session connected · petitions synchronized",
  DetailTitle: "The Guildmasters' Petition for Protected Grain Caravans",
  DetailBody: "Merchants request crown protection for a threatened supply route. In return they offer reduced wartime prices, priority provisioning for the garrison, and a bond forfeited if their promised deliveries fail.",
  DetailMeta: "AWAITING DECISION   ·   Due before sunset",
  AudienceSpeaker: "Lord Varmund of the Western Marches",
  AudienceText: "My liege, the guilds speak of charity, but their ledgers speak more plainly. Grant the escort only if their bond can feed every household that will bleed to protect it.",
  AudienceEmotion: "Measured, but openly skeptical",
  AudienceInput: "Answer Lord Varmund before the assembled court…",
  AudienceHasPortrait: true,
  AudiencePortraitId: "varmund_portrait",
  AudiencePortraitAsset: ASSETS.derthert,
  AudiencePortraitAdditionalArgs: "",
  AudiencePortraitTextureProviderName: "ReignPortraitProvider",
  AudiencePortraitCacheKey: "court_preview_varmund",
  PortraitCacheKey: "court_preview_portrait",

  Tabs: [
    { Label: "COURT", LockText: "", IsActive: true },
    { Label: "LANDS", LockText: "", IsActive: false },
    { Label: "SUBJECTS", LockText: "", IsActive: false },
    { Label: "REPUTATION", LockText: "", IsActive: false },
    { Label: "DIPLOMACY", LockText: "", IsActive: false },
    { Label: "AMBASSADORS", LockText: "", IsActive: false },
    { Label: "SPYMASTER", LockText: "", IsActive: false },
    { Label: "MILITARY", LockText: "", IsActive: false }
  ],
  Counters: [
    { Symbol: "G", Label: "FUNDS", Value: "84.5K", Trend: "+152 /day", IconSprite: "reign_court_status_funds", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "I", Label: "INFLUENCE", Value: "3,726", Trend: "+97 /day", IconSprite: "reign_court_status_influence", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "R", Label: "RENOWN", Value: "1,245", Trend: "+1.4 /day", IconSprite: "reign_court_status_renown", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "S", Label: "SUPPLY", Value: "12.4K", Trend: "+210 /day", IconSprite: "reign_court_status_supply", IsPresence: false, IsStrength: false, IsNotStrength: true },
    { Symbol: "M", Label: "STRENGTH", Value: "2,155", Trend: "+63 /day", IconSprite: "reign_court_status_strength", IsPresence: false, IsStrength: true, IsNotStrength: false },
    { Symbol: "C", Label: "COURT PRESENCE", Value: "+18", Trend: "public standing", IconSprite: "reign_court_status_presence", IsPresence: true, IsStrength: false, IsNotStrength: true }
  ],
  Lands: [
    { Title: "Pravend", Subtitle: "Food 245 / 300  ·  Grain 410", Status: "PROSPEROUS", Meta: "Prosperity 2,450  +3.1/day" },
    { Title: "Jaculan", Subtitle: "Food 188 / 250  ·  Grain 290", Status: "STABLE", Meta: "Prosperity 1,120  +1.2/day" },
    { Title: "Galend", Subtitle: "Food 205 / 270  ·  Grain 350", Status: "PROSPEROUS", Meta: "Prosperity 2,010  +2.4/day" },
    { Title: "Charas", Subtitle: "Food 76 / 240  ·  Grain 82", Status: "SHORTAGE", Meta: "Prosperity 870  -1.3/day" },
    { Title: "Lageta", Subtitle: "Food 191 / 260  ·  Grain 315", Status: "STABLE", Meta: "Prosperity 1,680  +1.6/day" }
  ],
  // The player-facing Royal Docket has not been implemented yet.
  // Keep the preview aligned with the live Court screen until it is.
  DailyAgenda: [],
  People: [
    { Title: "Lord Varmund", Subtitle: "Vassal\nLoyalty: 45", Meta: "Today", HasPortrait: true, ...portraitFields("varmund_portrait", ASSETS.derthert) },
    { Title: "Empress Rhagaea", Subtitle: "Foreign sovereign\nOpinion: +18", Meta: "Before noon", HasPortrait: true, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea) },
    { Title: "Master Eronys the Elder", Subtitle: "Treasurer\nThree favors owed", Meta: "Today", HasPortrait: true, ...portraitFields("eronys_portrait", ASSETS.lucon) },
    { Title: "Spymaster Abagai", Subtitle: "Spymaster\nIntelligence ready", Meta: "Today", HasPortrait: true, ...portraitFields("abagai_portrait", ASSETS.abagai) }
  ],
  DiplomaticStatus: [
    { Title: "Kingdom of Vlandia", Status: "FRIENDLY" },
    { Title: "Grand Principality of Sturgia", Status: "NEUTRAL" },
    { Title: "Aserai Sultanate", Status: "WARY" },
    { Title: "Khuzait Khanate", Status: "HOSTILE" },
    { Title: "Southern Empire", Status: "UNFRIENDLY" }
  ],
  AmbassadorCards: [
    { Title: "Ambassador Lucan", Subtitle: "Posted in Vlandia", Status: "POSTED" },
    { Title: "Ambassador Theron", Subtitle: "Traveling to distant Tyal", Status: "TRAVELING" },
    { Title: "Envoy Adalindis of Ocs Hall", Subtitle: "Awaiting formal credentials", Status: "PENDING" }
  ],
  ShowEmptyState: false,
  CanRemove: true,
  Ambassadors: [
    { KingdomName: "Western Empire", Name: "Mitela", ClanName: "Clan Comnos", PortraitId: "ira_portrait", PortraitAsset: ASSETS.ira, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_ira", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: true },
    { KingdomName: "Khuzait Khanate", Name: "Abagai", ClanName: "Clan Khergit", PortraitId: "abagai_portrait", PortraitAsset: ASSETS.abagai, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_abagai", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false },
    { KingdomName: "Battania", Name: "Corein", ClanName: "fen Gruffendoc", PortraitId: "corein_portrait", PortraitAsset: ASSETS.corein, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_corein", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false },
    { KingdomName: "Vlandia", Name: "Adalindis", ClanName: "House dey Rothad", PortraitId: "adalindis_portrait", PortraitAsset: ASSETS.adalindis, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_adalindis", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false },
    { KingdomName: "Southern Empire", Name: "Rhagaea", ClanName: "House Pethros", PortraitId: "rhagaea_portrait", PortraitAsset: ASSETS.rhagaea, PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", PortraitCacheKey: "ambassador_rhagaea", HasPortrait: true, BannerId: "player_clan_banner", BannerAsset: ASSETS.courtPlayerBanner, BannerArgs: "", BannerProvider: "BannerTableau", IsSelected: false }
  ],
  Rumors: [
    { Title: "Unrest is rising among the dockworkers of Charas.", Status: "MEDIUM" },
    { Title: "Lord Iric of Galend quietly seeks a marriage alliance.", Status: "LOW" },
    { Title: "Aserai nobles are purchasing horses and whispering of war.", Status: "HIGH" }
  ],
  TabRows: [
    { Title: "Petition of the Pravend Guildmasters", Subtitle: "A scheduled audience concerning food deliveries, caravan protection, and a forfeitable commercial bond.", Status: "AWAITING", Meta: "Due today", HasPortrait: true, ...portraitFields("guildmaster_portrait", ASSETS.adalindis) },
    { Title: "Steward of the Royal Household", Subtitle: "Major court office. Appointment and performance rules will be supplied by the selected tab's later design pass.", Status: "VACANT", Meta: "Appointment required", HasPortrait: false, ...portraitFields("", "") },
    { Title: "A promise made to Lord Varmund", Subtitle: "Pay 1,000 denars before the recorded deadline. Terms remain hashed and revision guarded.", Status: "UNRESOLVED", Meta: "14 days", HasPortrait: true, ...portraitFields("varmund_portrait", ASSETS.derthert) }
  ],
  Actions: [
    { Label: "ACCEPT WITH BOND", Description: "Approve the escort only after the guilds deposit the full forfeitable bond.", IsEnabled: true, StateText: "READY" },
    { Label: "REFUSE PETITION", Description: "Decline crown protection and leave the route to private guards.", IsEnabled: true, StateText: "READY" }
  ],

  ChatLines: [
    { Speaker: "Event", Text: "The Artisan Salon begins.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "Aevonos Aurelorides, Aevonara Aurelorides approach you.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "The event moves to the next phase: Demonstrations.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "The event moves to the next phase: Browsing and Bargaining.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
    { Speaker: "Event", Text: "Aevonos Aurelorides, Aevonara Aurelorides approach you.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
  ],
  PartyMembers: [
    { Name: "Mengus fen Gruffendoc", IsSelected: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active") },
    { Name: "Ira of the Southern Empire", IsSelected: true, ...portraitFields("ira_portrait", ASSETS.ira, "Watching") },
    { Name: "Corein, Daughter of Caladog", IsSelected: false, ...portraitFields("corein_portrait", ASSETS.corein, "Listening") },
    { Name: "Adalindis of Ocs Hall", IsSelected: false, ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Waiting") }
  ],
  Attendees: [
    { Name: "Mengus fen Gruffendoc", Subtitle: "Battanian lord", IsActive: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active conversation") },
    { Name: "Empress Rhagaea Pethros", Subtitle: "Sovereign of the Southern Empire", IsActive: false, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Available") },
    { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battanian noblewoman", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Available") }
  ],
  ActiveParticipants: [
    { Name: "Mengus fen Gruffendoc", Subtitle: "Battania · House Gruffendoc", IsActive: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active") },
    { Name: "Aevonos Aurelorides of Amitatys", Subtitle: "Western Empire · House Comnos", IsActive: true, ...portraitFields("aevonos_portrait", ASSETS.lucon, "Active") }
  ],
  AvailableAttendees: [
    { Name: "Empress Rhagaea Pethros", Subtitle: "Southern Empire · House Pethros", IsActive: false, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Available") },
    { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battania · fen Gruffendoc", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Available") },
    { Name: "Abagai of the Khergit Borderlands", Subtitle: "Khuzait Khanate · Baltait", IsActive: false, ...portraitFields("abagai_portrait", ASSETS.abagai, "Available") },
    { Name: "Adalindis of Ocs Hall and the Western March", Subtitle: "Vlandia · House dey Rothad", IsActive: false, ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Available") },
    { Name: "High King Caladog fen Gruffendoc", Subtitle: "Battania · Royal House", IsActive: false, ...portraitFields("caladog_portrait", ASSETS.caladog, "Available") }
  ],
  Contacts: [
    { Name: "Mengus fen Gruffendoc", Subtitle: "Battanian lord · last met at Marunath", IsSelected: true, IsUnselected: false, HasUnread: true, UnreadText: "2 unread", ...correspondencePortraitFields("mengus_portrait", ASSETS.mengus, "Friendly · 42") },
    { Name: "Empress Rhagaea Pethros", Subtitle: "Southern Empire · diplomatic correspondence", IsSelected: false, IsUnselected: true, HasUnread: false, UnreadText: "", ...correspondencePortraitFields("rhagaea_portrait", ASSETS.rhagaea, "Cordial · 18") },
    { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battania · trusted correspondent", IsSelected: false, IsUnselected: true, HasUnread: true, UnreadText: "1 unread", ...correspondencePortraitFields("corein_portrait", ASSETS.corein, "Friendly · 51") },
    { Name: "Lady Adalindis of Ocs Hall", Subtitle: "Vlandia · court and trade matters", IsSelected: false, IsUnselected: true, HasUnread: false, UnreadText: "", ...correspondencePortraitFields("adalindis_portrait", ASSETS.adalindis, "Neutral · 4") },
    { Name: "An unmet correspondent", Subtitle: "Introduced by letter", IsSelected: false, IsUnselected: true, HasUnread: false, UnreadText: "", PortraitCacheKey: "", PortraitAsset: "", Status: "Not yet met" },
    { Name: "A known resident", Subtitle: "No AI portrait available", IsSelected: false, IsUnselected: true, HasUnread: false, UnreadText: "", PortraitCacheKey: "preview_missing_ai", PortraitAsset: "", Status: "No correspondence" }
  ],
  SelectedName: "Mengus fen Gruffendoc",
  SelectedSubtitle: "Battanian lord · correspondence carried by trusted courier",

  ShowTarget: true,
  Outcome: "A Defensive Compact Has Been Proclaimed",
  Summary: "Aeric fen Seanel and Empress Rhagaea have agreed to protect the Amitatys grain road for one campaigning season. Both parties gain safer commerce, but the compact may alarm neighboring rulers who see an alliance where its signatories describe only practical cooperation.",
  Terms: "Joint patrols begin within seven days. Aeric supplies 120 mounted troops; Rhagaea supplies 180 infantry and the route's quartermasters. Command alternates every thirty days. Either party may withdraw after giving fourteen days' notice.",
  ActorName: "Aeric fen Seanel",
  ActorKingdom: "Battania · Clan Seanel",
  ActorReason: "Seeks secure grain deliveries before the western garrisons exhaust their stores.",
  ActorPortraitId: "actor_portrait",
  ActorPortraitAsset: ASSETS.aeron,
  ActorPortraitArgs: "",
  ActorPortraitProvider: "ReignPortraitProvider",
  ActorPortraitCacheKey: "diplomacy_actor_aeric",
  ActorBannerId: "actor_banner",
  ActorBannerArgs: "",
  ActorBannerProvider: "BannerTableau",
  TargetName: "Empress Rhagaea Pethros",
  TargetKingdom: "Southern Empire · House Pethros",
  TargetReason: "Needs a dependable western route that does not depend on Lucon's goodwill.",
  TargetPortraitId: "target_portrait",
  TargetPortraitAsset: ASSETS.rhagaea,
  TargetPortraitArgs: "",
  TargetPortraitProvider: "ReignPortraitProvider",
  TargetPortraitCacheKey: "diplomacy_target_rhagaea",
  TargetBannerId: "target_banner",
  TargetBannerArgs: "",
  TargetBannerProvider: "BannerTableau"
};

export const ASSET_BY_IMAGE_ID = {
  player_portrait: ASSETS.aeron,
  mengus_portrait: ASSETS.mengus,
  ira_portrait: ASSETS.ira,
  corein_portrait: ASSETS.corein,
  caladog_portrait: ASSETS.caladog,
  derthert_portrait: ASSETS.derthert,
  diplomacy_actor_aeric: ASSETS.aeron,
  diplomacy_target_rhagaea: ASSETS.rhagaea,
  aeron_portrait: ASSETS.aeron,
  rhagaea_portrait: ASSETS.rhagaea,
  abagai_portrait: ASSETS.abagai,
  spymaster_portrait: ASSETS.abagai,
  adalindis_portrait: ASSETS.adalindis,
  actor_portrait: ASSETS.aeron,
  target_portrait: ASSETS.rhagaea,
  preview_portrait: ASSETS.corein,
  zoom_portrait: ASSETS.mengus,
  event_background: ASSETS.eventArt,
  feast_empire_toasts_and_table_talk: ASSETS.eventArt,
  "reigneventart|feast_empire|toasts_and_table_talk": ASSETS.eventArt,
  generated_wilderness_forest: ASSETS.wildernessForest,
  generated_wilderness_background: ASSETS.wildernessBackground,
  "reigneventart|castle_map|empire": ASSETS.castleMapEmpire,
  "reigneventart|war_council|map": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_calradia.png",
  "reigneventart|war_council|outer_frame": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_outer_frame.png",
  "reigneventart|war_council|raven_scroll": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_raven_scroll.png",
  "reigneventart|war_council|panel_frame": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_panel_frame.png",
  "reigneventart|war_council|panel_frame_tall": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_panel_frame_tall.png",
  "reigneventart|war_council|panel_frame_overlay": "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_panel_frame_overlay.png"
};

// Bannerlord replaces these TextureWidget images by widget identity at runtime
// (see PortraitPatch.TryApplyChatBackground). The previewer mirrors that
// contract so the XML binding used as a provider hook does not become the
// visible portrait asset in the browser.
export const ASSET_BY_WIDGET_ID = {
  ReignIndividualChatBackground: ASSETS.individualChatBackground,
  WarCouncilMap: "../../GUI/SpriteParts/ui_reignbeta_war_council/reign_war_council_calradia.png"
};

const peaceForceOptions = selected => [null, ...Array.from({ length: 11 }, (_, i) => 40 + 5 * i)].map(percent => ({
  IsSelected: percent === selected, Label: (percent == null ? 'Automatic' : `${percent}%`) + (percent === selected ? ' •' : '')
}));
const PREVIEW_PRESET_OVERRIDES = {
  "war-council-peace-40": { PeaceStandingSelected: true, WarStandingSelected: false, PeaceStandingUnselected: false, WarStandingUnselected: true, CanChoosePeaceForce: true, PeaceForceText: "40% v", PeaceForceOptions: peaceForceOptions(40) },
  "war-council-peace-90": { PeaceStandingSelected: true, WarStandingSelected: false, PeaceStandingUnselected: false, WarStandingUnselected: true, CanChoosePeaceForce: true, PeaceForceText: "90% v", PeaceForceOptions: peaceForceOptions(90) },
  "war-council-peace-dropdown": { PeaceStandingSelected: true, WarStandingSelected: false, PeaceStandingUnselected: false, WarStandingUnselected: true, CanChoosePeaceForce: true, PeaceForceText: "60% v", StandingDropdownVisible: true, PeaceForceOptions: peaceForceOptions(60) },
  "war-council-standing-readonly": { CanChangeStanding: false, CanChoosePeaceForce: false },
  "war-council-no-realm": { CanChangeStanding: false, CanChoosePeaceForce: false, PeaceStandingSelected: false, WarStandingSelected: false, PeaceStandingUnselected: true, WarStandingUnselected: true, PeaceForceText: "--" },
  "court-life-international": courtLifePreview("INTERNATIONAL", "A Border Complaint", [
    { Name: "Lady Adalindis", Subtitle: "Your noble", ...portraitFields("adalindis_portrait", ASSETS.adalindis) },
    { Name: "Rhagaea's Envoy", Subtitle: "Foreign ambassador", ...portraitFields("rhagaea_portrait", ASSETS.rhagaea) }
  ], "My ruler asks that you hear the merchants before granting compensation. We can compare their accounts here.", "Review the complaint before confirming a settlement.", "SERIOUS"),
  "court-life-family": courtLifePreview("FAMILY VISIT", "A Small Discovery", [
    { Name: "Your young child", Subtitle: "Child, age 4", HasPortrait: false, PortraitId: "", PortraitCacheKey: "", PortraitAsset: "" }
  ], "Look! The beetle has tiny feet. Can we find its house after court?", "Spend time together; plans for later remain a personal conversation."),
  "court-life-patronage": courtLifePreview("PATRONAGE", "Stories for the Road", [
    { Name: "Mira, storyteller of Zeonica", Subtitle: "Storyteller", HasPortrait: false, PortraitId: "", PortraitCacheKey: "", PortraitAsset: "" }
  ], "I can begin with a verse here, or take the tale through your towns. Which audience should I prepare for?", "Kingdom-wide: Praise (8000 gold)", "AT COURT", {
    GoldGrantVisible: true, CanGrantWithGold: true, GoldGrantLabel: "KINGDOM-WIDE: PRAISE (8000 GOLD)",
    PurposeText: "+3 noble relation toward the ruler per noble reached. Delivery: 7 day(s). Current eligible audience: 24 nobles."
  }),
  "court-life-visitors": courtLifePreview("NOBLE VISITORS", "A Visiting Household", [
    { Name: "Lord Mengus", Subtitle: "Parent", ...portraitFields("mengus_portrait", ASSETS.mengus) },
    { Name: "Lady Adalindis", Subtitle: "Parent", ...portraitFields("adalindis_portrait", ASSETS.adalindis) },
    { Name: "Lady Corein", Subtitle: "Adult child, age 23", ...portraitFields("corein_portrait", ASSETS.corein) },
    { Name: "Lady Abagai", Subtitle: "Adult child, age 21", ...portraitFields("abagai_portrait", ASSETS.abagai) }
  ], "We thank you for receiving our household. We will remain in the city for five days, and hope to attend your court again.", "Welcome the household or conclude the audience."),
  "court-military-standing": {
    HomeVisible: false, LargeHomeVisible: false, ReferenceHomeVisible: false,
    TabContentVisible: true, DetailVisible: true, AudienceVisible: false, ActionsVisible: true,
    ScreenTitle: "MILITARY", ScreenSubtitle: "ARMIES, PARTIES, READINESS AND SUPPLIES",
    DetailTitle: "Kingdom Military Standing", DetailMeta: "PEACE STANDING",
    DetailBody: "Peace standing keeps smaller field forces and releases the least experienced healthy surplus troops. Hostile political pressure raises readiness on the relevant frontier. The sovereign's selected peace target remains in force during war.\nGarrison targets remain full. Quartered regulars cost less in wages and supplies. A fixed share of discharged manpower returns to functioning villages.",
    TabRows: [{ Title: "Kingdom Military Standing", Subtitle: "Review field readiness and permanent defence.", Status: "PEACE STANDING", Meta: "Sovereign order", HasPortrait: false, ...portraitFields("", "") }],
    Actions: [
      { Label: "ORDER PEACE STANDING", Description: "Restore the saved peace target, including during war; garrisons keep full targets.", IsEnabled: true, StateText: "READY" },
      { Label: "ORDER WAR STANDING", Description: "Permit full field recruitment. Actual recruits, provisions and clan funding are required.", IsEnabled: true, StateText: "READY" }
    ]
  },
  "court-history": {
    HomeVisible: false,
    LargeHomeVisible: false,
    ReferenceHomeVisible: false,
    TabContentVisible: true,
    DetailVisible: true,
    AudienceVisible: false,
    ActionsVisible: false,
    ScreenTitle: "COURT HISTORY",
    ScreenSubtitle: "ROYAL COURT AT ZEONICA",
    DetailTitle: "Petition of Temeon the Brewer",
    DetailMeta: "GRANTED · 2 SEPTEMBER 1092",
    DetailBody: "The crown approved emergency food relief for Zeocorys. Forty measures were committed from the capital stores, and the ruling was entered into the permanent court record.",
    TabRows: [
      { Title: "Temeon the Brewer", Subtitle: "Food relief for Zeocorys was granted from the capital stores.", Status: "GRANTED", Meta: "2 September 1092", HasPortrait: true, ...portraitFields("temeon_portrait", ASSETS.mengus) },
      { Title: "Emergency Chancellor", Subtitle: "Lady Adalindis assumed the seals during the Chancellor's incapacity.", Status: "RECORDED", Meta: "1 September 1092", HasPortrait: true, ...portraitFields("emergency_chancellor_portrait", ASSETS.adalindis) },
      { Title: "Garrison Escort Returned", Subtitle: "The randomly selected Zeonica escort returned after completing its service.", Status: "COMPLETE", Meta: "31 August 1092", HasPortrait: false, ...portraitFields("", "") }
    ],
    Actions: []
  },
  "pregnancy-warning": {
    IsPregnancyWarningVisible: true,
    InputEnabled: false,
    IsBusy: true,
    BusyText: "Choose whether to proceed."
  },
  "generation-failed": {
    IsBusy: false, InputEnabled: true, InputText: "", SendButtonText: "Retry",
    BusyText: "Codex temporarily limited this request (429). Your message is saved. Select Retry.",
    ChatLines: [
      { Speaker: "Michael", Text: "I will meet you at the eastern gate.", RichText: "I will meet you at the eastern gate.", Role: "player", IsPlayerLine: true, IsNpcLine: false, IsSystemLine: false },
      { Speaker: "Bannerlord Reign", Text: "Codex temporarily limited this request (429). Your message is saved. Select Retry to try this turn again, or write a new message.", RichText: "Codex temporarily limited this request (429). Your message is saved. Select Retry to try this turn again, or write a new message.", Role: "system", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
    ]
  },
  "recovery-available": {
    IsBusy: false, InputEnabled: true, InputText: "", SendButtonText: "Recover", BusyText: "",
    ChatLines: [
      { Speaker: "Councillor", Text: "What brings you here today?", RichText: "What brings you here today?", Role: "npc", IsPlayerLine: false, IsNpcLine: true, IsSystemLine: false },
      { Speaker: "Bannerlord Reign", Text: "1 saved message(s) have unconfirmed delivery. With an empty message box, select Recover to review the newest draft. Nothing is sent automatically.", RichText: "1 saved message(s) have unconfirmed delivery. With an empty message box, select Recover to review the newest draft. Nothing is sent automatically.", Role: "system", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
    ]
  },
  "recovery-review": {
    IsBusy: false, InputEnabled: true, InputText: "Good morning.", SendButtonText: "Send",
    BusyText: "Earlier delivery is unconfirmed. Review the draft; Send submits a new message and can repeat actions.",
    ChatLines: [
      { Speaker: "Bannerlord Reign", Text: "A saved message is being reviewed in the message box. It has not been submitted in this conversation.", RichText: "A saved message is being reviewed in the message box. It has not been submitted in this conversation.", Role: "system", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
    ]
  },
  "outfit-input": { IsOutfitVisible: true, IsOutfitInputVisible: true, InputEnabled: false },
  "outfit-changing": { IsOutfitVisible: true, IsOutfitComparisonVisible: true, IsOutfitChanging: true, InputEnabled: false },
  "outfit-ready": { IsOutfitVisible: true, IsOutfitComparisonVisible: true, IsOutfitReady: true, OutfitAfterPath: ASSETS.spymasterFullBody, InputEnabled: false },
  "outfit-failed": { IsOutfitVisible: true, IsOutfitComparisonVisible: true, OutfitStatus: "The new portrait could not be generated. Please try again.", InputEnabled: false },
  "outfit-ineligible": { CanChangeOutfit: false },
  "training-yard-empty": {
    TrainerCount: 0,
    TroopCount: 0,
    Trainers: [],
    Troops: [],
    SelectedTrainerName: "NO TRAINER SELECTED",
    SelectedTrainerDetail: "No eligible adult NPC is available at this keep.",
    LeadershipText: "LEADERSHIP —",
    WeaponText: "BEST WEAPON —",
    RateText: "0 XP PER TROOP / HOUR",
    ElapsedText: "ELAPSED  0 HOURS",
    DeliveredText: "DELIVERED  0 XP",
    StatusText: "No eligible trainer or upgradeable troop is available.",
    CanTrain: false,
    CanStop: false,
    HasSelectedTrainer: false,
    HasSelectedGeneratedPortrait: false,
    PortraitAsset: ""
  },
  "training-yard-active": {
    IsTraining: true,
    CanTrain: false,
    CanStop: true,
    ElapsedText: "ELAPSED  37 HOURS",
    DeliveredText: "DELIVERED  48,372 XP",
    StatusText: "Training continues; campaign food, wages, quests, and world simulation are advancing."
  },
  "spymaster-people": {
    IsIntelligence: true,
    IsSubterfuge: false,
    IsReputation: false,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: false,
    IsPeople: true,
    ShowPeopleGroups: true,
    PeopleGroupText: "FOREIGN NOBLES",
    TargetName: "Branara",
    TargetDetail: "Foreign noble · House dey Meroc",
    ActionName: "Investigate relationships",
    OperationDetail: "900 denars  •  3.0 days  •  Success 68%  •  Detection 14%  •  ROUTINE",
    AssessmentText: "ROUTINE"
  },
  "spymaster-subterfuge": {
    IsIntelligence: false,
    IsSubterfuge: true,
    IsReputation: false,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: false,
    IsPeople: false,
    ShowPeopleGroups: false,
    ShowSocialItem: false,
    TargetName: "Akkalat",
    TargetDetail: "FOREIGN — TOWN · Tigit; governor Chambui.",
    ActionName: "Disrupt food",
    OperationDetail: "7,500 denars  •  7.0 days  •  Success 36%  •  Detection 47%  •  Difficult  •  Capacity 0/3",
    AssessmentText: "DIFFICULT",
    IsOrdinaryAssessment: false,
    IsChallengingAssessment: true
  },
  "spymaster-reputation": {
    IsIntelligence: false,
    IsSubterfuge: false,
    IsReputation: true,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: false,
    IsPeople: false,
    ShowPeopleGroups: false,
    ShowSocialItem: true,
    SocialScopeText: "FOREIGN LORDS",
    SocialItemName: "Select rumor or reputation",
    TargetName: "Aeric fen Seanel",
    TargetDetail: "Ruler of Battania",
    ActionName: "Mitigate one of your rumors",
    OperationDetail: "1,200 denars  •  4.0 days  •  Success 61%  •  Detection 18%  •  GUARDED",
    AssessmentText: "GUARDED"
  },
  "spymaster-archive": {
    IsIntelligence: false,
    IsSubterfuge: false,
    IsReputation: false,
    IsArchive: true,
    ShowOperationPanel: false,
    IsLands: false,
    IsPeople: false,
    ShowPeopleGroups: false,
    ReportText: "Agents confirmed the garrison rotation, granary reserves, and western gate watch schedule. No operative was exposed.",
    Reports: [
      { Title: "Intelligence: Ab Comer Castle", StateText: "COMPLETED", DateText: "12th of Summer, 1092", IsSelected: true },
      { Title: "Rumor inquiry: Branara", StateText: "COMPLETED", DateText: "8th of Summer, 1092", IsSelected: false },
      { Title: "Counterintelligence: Zeonica", StateText: "COMPLETED", DateText: "1st of Summer, 1092", IsSelected: false }
    ]
  },
  wilderness: {
    ShowPhaseControls: false,
    ShowWildernessArt: true,
    EventTitle: "Wilderness Encounter",
    PhaseTitle: "A Voice Beyond the Treeline",
    PhaseStatus: "FOREST TRACK · LATE AFTERNOON",
    PhaseDescription: "A chance meeting interrupts the road before the light fades beneath the trees.",
    EventImageId: "generated_wilderness_forest",
    EventImageAsset: ASSETS.wildernessForest,
    EventImageText: "A Stranger at the Treeline\nForest Road · Late Afternoon",
    EventBackgroundImageId: "generated_wilderness_background",
    BackgroundImageId: "generated_wilderness_background",
    BackgroundAsset: ASSETS.wildernessBackground,
    AttendeeListTopMargin: 492,
    AttendeeListBottomMargin: 16,
    AttendeeTitleTopMargin: 452,
    EventArtTopMargin: 12,
    ActiveParticipants: [
      { Name: "Adalindis of Ocs Hall and the Western March", Subtitle: "Vlandia · House dey Rothad", IsActive: true, ...portraitFields("adalindis_portrait", ASSETS.adalindis, "Speaking") }
    ],
    AvailableAttendees: [
      { Name: "Mengus fen Gruffendoc", Subtitle: "Battania · House Gruffendoc", IsActive: false, ...portraitFields("mengus_portrait", ASSETS.mengus, "Nearby") },
      { Name: "Corein, Daughter of High King Caladog", Subtitle: "Battania · fen Gruffendoc", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Nearby") }
    ],
    ChatLines: [
      { Speaker: "Event", Text: "Your party slows as movement stirs beyond the treeline.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true },
      { Speaker: "Adalindis", Text: "My lord, I had not expected another company on this road before nightfall.", IsPlayerLine: false, IsNpcLine: true, IsSystemLine: false },
      { Speaker: "Event", Text: "The forest falls quiet while both parties decide whether to approach.", IsPlayerLine: false, IsNpcLine: false, IsSystemLine: true }
    ]
  }
};

const PREFAB_SAMPLE_OVERRIDES = {
  "ReignTavernHouseScreen.xml": getTavernHouseSampleData(),
  "ReignClanAccordsScreen.xml": getClanAccordsSampleData(),
  "AIPortraitsMemoriesBook.xml": {
    TitleText: "MEMORIES BOOK",
    CounterText: "1 / 3",
    CaptionText: "A formal pact is proclaimed before the assembled lords of Calradia.",
    HasMemories: true,
    MemoryBookImageId: "reigneventart|feast_empire|toasts_and_table_talk",
    MemoryItems: [
      { ListText: "A Pact Proclaimed Before the Lords of Calradia", IsSelected: true },
      { ListText: "The Council Supper at Zeonica", IsSelected: false },
      { ListText: "A Quiet Road Beyond the Southern Gate", IsSelected: false }
    ]
  },
  "ReignAmbassadorScreen.xml": {
    AmbassadorCount: SAMPLE_DATA.Ambassadors.length,
    PlayerBannerId: "player_clan_banner",
    PlayerBannerAsset: ASSETS.courtPlayerBanner,
    PlayerBannerArgs: "",
    PlayerBannerProvider: "BannerTableauTextureProvider",
    ForeignAdvisor: {
      Name: "Branara",
      Status: "AVAILABLE",
      HasPortrait: true,
      PortraitCacheKey: "preview_foreign_advisor_branara",
      PortraitId: "corein_portrait",
      PortraitAsset: ASSETS.corein,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    Ambassadors: SAMPLE_DATA.Ambassadors.map((ambassador) => ({
      ...ambassador,
      StatusText: ambassador.Status,
      CanDismiss: true,
      HasCharacterModel: false,
      AmbassadorModel: {
        HasCharacterModel: false,
        BannerCodeText: "",
        BodyProperties: "",
        CharStringId: "",
        EquipmentCode: "",
        IsFemale: false,
        MountCreationKey: "",
        StanceIndex: 0,
        ArmorColor1: "#473421FF",
        ArmorColor2: "#8C733DFF",
        Race: 0,
        IsTableauEnabled: false
      }
    })),
    ShowEmptyState: false,
    StatusText: "Resident envoys are available for official audience."
  },
  "ReignFamilyChambersScreen.xml": {
    Title: "FAMILY CHAMBERS",
    Subtitle: "THE ROYAL HOUSEHOLD AT ZEONICA",
    Adults: [
      { Name: "Gwydan", Detail: "Spouse · present in the royal household", Relation: "RELATION 82", IsSelected: true, ...portraitFields("mengus_portrait", ASSETS.mengus) },
      { Name: "Elideth", Detail: "Sister · economic advisor", Relation: "RELATION 61", IsSelected: false, ...portraitFields("adalindis_portrait", ASSETS.adalindis) }
    ],
    Children: [
      { Name: "Aeron", AgeText: "AGE 12", Parents: "Gwydan and the sovereign", IsSelected: false, ...portraitFields("aeron_portrait", ASSETS.aeron) },
      { Name: "Ceryn", AgeText: "AGE 7", Parents: "Gwydan and the sovereign", IsSelected: true, ...portraitFields("corein_portrait", ASSETS.corein) }
    ],
    SelectionText: "2 family members selected",
    StatusText: "Choose family members to enter the chambers.",
    IsBusy: false,
    CanEnterScene: true
  },
  "ReignTrainingYardScreen.xml": {
    Title: "TRAINING YARD",
    TrainerHeading: "TRAINERS",
    TroopHeading: "PARTY TROOPS",
    SelectedTrainerName: "Tacteos",
    SelectedTrainerDetail: "Companion · travelling with the main party",
    LeadershipText: "LEADERSHIP 150",
    WeaponText: "POLEARM 200",
    RateText: "58 XP PER TROOP / HOUR",
    ElapsedText: "ELAPSED  11 HOURS",
    DeliveredText: "DELIVERED  9,628 XP",
    StatusText: "Tacteos is drilling every troop that can still gain upgrade XP.",
    IsTraining: false,
    CanTrain: true,
    CanStop: false,
    HasSelectedTrainer: true,
    HasSelectedGeneratedPortrait: true,
    SelectedPortraitCacheKey: "preview_training_yard_tacteos",
    SelectedPortraitId: "mengus_portrait",
    SelectedPortraitAdditionalArgs: "",
    SelectedPortraitTextureProviderName: "ReignPortraitProvider",
    PortraitAsset: ASSETS.mengus,
    TrainerCount: 10,
    TroopCount: 12,
    Trainers: [
      { Name: "Tacteos", Detail: "Leadership 150 · Polearm 200", Rate: "58 XP / TROOP / HOUR", IsSelected: true, CanSelect: true, ...portraitFields("mengus_portrait", ASSETS.mengus) },
      { Name: "Corein, Daughter of High King Caladog", Detail: "Leadership 108 · Two Handed 178", Rate: "47 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("corein_portrait", ASSETS.corein) },
      { Name: "Aeron the Far-Travelled Shield Brother", Detail: "Leadership 72 · Bow 124", Rate: "32 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("aeron_portrait", ASSETS.aeron) },
      { Name: "Adalindis", Detail: "Leadership 94 · Crossbow 132", Rate: "37 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("adalindis_portrait", ASSETS.adalindis) },
      { Name: "Caladog", Detail: "Leadership 181 · Two-Handed 210", Rate: "65 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("caladog_portrait", ASSETS.caladog) },
      { Name: "Rhagaea", Detail: "Leadership 196 · One-Handed 188", Rate: "64 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea) },
      { Name: "Derthert", Detail: "Leadership 167 · Polearm 176", Rate: "57 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("derthert_portrait", ASSETS.derthert) },
      { Name: "Abagai", Detail: "Leadership 121 · Bow 191", Rate: "52 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("abagai_portrait", ASSETS.abagai) },
      { Name: "Lucon", Detail: "Leadership 143 · One-Handed 155", Rate: "49 XP / TROOP / HOUR", IsSelected: false, CanSelect: true, ...portraitFields("lucon_portrait", ASSETS.lucon) }
    ],
    Troops: [
      { Name: "Imperial Elite Cataphract", TierText: "TIER 6", CountText: "18 TROOPS", ProgressText: "READY TO UPGRADE", StateText: "XP CAPPED", CanGainXp: false },
      { Name: "Battanian Trained Spearman", TierText: "TIER 3", CountText: "46 TROOPS", ProgressText: "612 / 900 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Vlandian Hardened Crossbowman of the Western Marches", TierText: "TIER 4", CountText: "27 TROOPS", ProgressText: "1,018 / 1,300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Khuzait Nomad", TierText: "TIER 1", CountText: "53 TROOPS", ProgressText: "144 / 300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Aserai Veteran Infantry", TierText: "TIER 5", CountText: "22 TROOPS", ProgressText: "1,700 / 1,700 XP", StateText: "XP CAPPED", CanGainXp: false },
      { Name: "Sturgian Woodsman", TierText: "TIER 2", CountText: "31 TROOPS", ProgressText: "210 / 550 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Imperial Recruit", TierText: "TIER 1", CountText: "19 TROOPS", ProgressText: "88 / 300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Battanian Fian", TierText: "TIER 5", CountText: "12 TROOPS", ProgressText: "1,211 / 1,700 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Vlandian Footman", TierText: "TIER 2", CountText: "38 TROOPS", ProgressText: "447 / 550 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Khuzait Horse Archer", TierText: "TIER 4", CountText: "21 TROOPS", ProgressText: "908 / 1,300 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Aserai Mameluke Cavalry", TierText: "TIER 5", CountText: "14 TROOPS", ProgressText: "1,350 / 1,700 XP", StateText: "RECEIVING TRAINING", CanGainXp: true },
      { Name: "Sturgian Heroic Line Breaker", TierText: "TIER 5", CountText: "9 TROOPS", ProgressText: "1,699 / 1,700 XP", StateText: "RECEIVING TRAINING", CanGainXp: true }
    ]
  },
  "ReignPortraitRefreshPopup.xml": {
    Title: "LOOK AGAIN?",
    Detail: "You already have an image of Rhagaea. Look Again replaces it using their current native appearance. Reflect on the Years changes only their apparent age.",
    LookAgainText: "Look Again", ReflectText: "Reflect on the Years", CancelText: "CANCEL", CanReflect: true
  },
  "ReignPlayerApproachPopup.xml": {
    Title: "A REQUEST TO SPEAK", Detail: "Rhagaea is asking to speak with you.", AcceptText: "Talk to them", DeclineText: "Tell them another time"
  },
  "ReignBanterPanel.xml": {
    Title: "Party banter", Collapsed: false, Expanded: true, PanelLeft: 1234, PanelTop: 599, PanelWidth: 420, PanelHeight: 220, DragWidth: 256, TitleWidth: 220, TranscriptWidth: 390, TranscriptHeight: 144, StatusWidth: 344, CollapseText: "Minimize", CanJoin: true, ChatScrollVersion: 1, Status: "",
    Transcript: "Rhagaea: Even a crown does little to keep the rain out.\n\nJon: I could hold a cloak over it, Your Majesty.\n\nRhagaea: See to your own boots first."
  },
  "ReignNotableGenerationPopup.xml": {
    Title: "PREPARING CALRADIA",
    Detail: "Generating the notable characters and relationships needed for this campaign.",
    Progress: "Building notable 18 of 42",
    HasError: false
  },
  "ReignCastleLayoutScreen.xml": {
    CastleMapImageId: "reigneventart|castle_map|empire",
    GovernmentButtonText: "GOVERNMENT — RULER ACCESS",
    GuestBedroomRow1: emptyCastleSlots(5),
    GuestBedroomRow2: emptyCastleSlots(5),
    GuestBedroomRow3: emptyCastleSlots(5),
    GuestBedroomRow4: emptyCastleSlots(5)
  },
  "ReignGovernmentScreen.xml": {
    Title: "SOUTHERN EMPIRE GOVERNMENT",
    ModeText: "RULER WORKING SESSION",
    InstitutionText: "Imperial Senate",
    AuthorityText: "AUTHORITY 4 / 5",
    TrustText: "TRUST 63 / 100",
    StanceText: "RULER STANCE -12  •  Common Weal Coalition",
    MeetingText: "NEXT SEASONAL MEETING: DAY 211.0",
    StatusText: "The senate is debating relief for a recently raided village.",
    LobbyTerms: "Fund the western granary before day 218",
    IsInteractive: true,
    IsReadOnly: false,
    HasSelectedResolution: true,
    HasSelectedMember: true,
    HasActionPressure: true,
    CanOverrideActionPressure: true,
    ActionPressureText: "War — 38% support — approval or override required",
    SelectedResolutionTitle: "Relief for the Raided Villages",
    SelectedResolutionDetail: "Settlement relief — evidence: village_raided\n\nROUTE I: Reduce food dues for one season\nROUTE II: Make a one-time granary donation",
    SelectedMemberText: "Senator Oros — Common Weal Coalition",
    MemberCount: 2,
    Parties: [
      { Name: "Common Weal Coalition", Planks: "PopularWelfare  •  Trade  •  Infrastructure", Speaker: "Speaker: Senator Oros  •  Seats: 9", Alignment: "LEADING", Statement: "Relief must reach the western villages before winter." },
      { Name: "Crown and Legion", Planks: "RoyalAuthority  •  MilitaryStrength  •  Justice", Speaker: "Speaker: Senator Vala  •  Seats: 6", Alignment: "COALITION", Statement: "The throne may act, but the frontier must not be stripped." },
      { Name: "Free Estates", Planks: "LocalAutonomy  •  Trade  •  RepresentativeAuthority", Speaker: "Speaker: Senator Nemos  •  Seats: 4", Alignment: "OPPOSITION", Statement: "Any levy must be bounded and recorded." }
    ],
    Resolutions: [
      { Title: "Relief for the Raided Villages", Summary: "DEBATE  •  Party common_weal", FullDetail: "Two trackable routes", IsSelected: true },
      { Title: "Reinforce the Western Garrison", Summary: "ACTIVE  •  Party crown_legion", FullDetail: "Progress 114 / 160", IsSelected: false }
    ],
    Members: [
      { Name: "Senator Oros", Detail: "Common Weal Coalition  •  Party loyalty 76  •  Government loyalty 68  •  Ruler relation 14", IsSelected: true },
      { Name: "Senator Vala", Detail: "Crown and Legion  •  Party loyalty 81  •  Government loyalty 54  •  Ruler relation -8", IsSelected: false }
    ]
  },
  "ReignCourtScreen.xml": {
    DocketTitle: "ROYAL DOCKET",
    LocalModeNoticeVisible: false,
    RoyalCommandsVisible: true,
    HasChancellor: true,
    ChancellorName: "Lady Adalindis",
    ChancellorPortraitCacheKey: "preview_chancellor_adalindis",
    ChancellorStateText: "INACTIVE • ACTIVE NEXT 8 AM",
    ChancellorSalaryText: "500 denars per Active day",
    ChancellorToggleVisible: true,
    DocketItemCount: 2,
    DocketScrollVisible: false,
    DailyAgenda: [
      {
        Title: "Guildmaster Eronys",
        Subtitle: "A grain shortage is reducing hearth recovery at Ortysia's dependent village.",
        Status: "SERIOUS",
        Meta: "30 food • 7 days",
        IconSprite: "reign_court_docket_accounts"
      },
      {
        Title: "Headman Mengus",
        Subtitle: "Raiding has left Marunath's approaches unsafe for villagers and caravans.",
        Status: "SEVERE",
        Meta: "24 soldiers • 10 days",
        IconSprite: "reign_court_docket_military"
      }
    ]
  },
  "ReignCourtPetitionScreen.xml": {
    ActionComposerId: "preview-action-composer",
    Title: "COURT PETITION",
    PetitionerName: "Mengus fen Gruffendoc",
    PetitionerRole: "Petitioner for Ortysia",
    SeverityText: "SERIOUS NEED",
    ProblemText: "The dependent villages cannot replace seed grain lost during the last raids.",
    RequestTypeText: "FOOD RELIEF",
    RequestedResourceText: "30 food from the capital stores for 7 days",
    PurposeText: "Ortysia",
    NeedFacts: "Observed 18.4 • healthy reference 42.0 • normalized need 56%",
    RequestTerms: "30 food from the capital stores for 7 days",
    BenefitText: "If granted: +4 petitioner relation, +1 associated notable relation, and +0.35 hearth growth per day through the commitment.",
    DangerText: "",
    WarningText: "The capital retains a positive food reserve after this grant.",
    EventImageId: "preview_ruler_petition_vlandia_throne_room",
    EventImageAsset: ASSETS.rulerPetitionCourt,
    PortraitCacheKey: "preview_petitioner_eronys",
    PortraitId: "preview_petitioner_native",
    PortraitAsset: ASSETS.mengus,
    PortraitAdditionalArgs: "",
    PortraitTextureProviderName: "CharacterImageTextureProvider",
    HasPortrait: true,
    ActiveParticipants: [
      { Name: "Mengus fen Gruffendoc", Subtitle: "Petitioner for Ortysia", IsActive: true, ...portraitFields("mengus_portrait", ASSETS.mengus, "Active") },
      { Name: "Aevonos Aurelorides", Subtitle: "Court noble", IsActive: true, ...portraitFields("aevonos_portrait", ASSETS.lucon, "Active") }
    ],
    AvailableAttendees: [
      { Name: "Empress Rhagaea Pethros", Subtitle: "Court noble", IsActive: false, ...portraitFields("rhagaea_portrait", ASSETS.rhagaea, "Available") },
      { Name: "Corein fen Gruffendoc", Subtitle: "Court noble", IsActive: false, ...portraitFields("corein_portrait", ASSETS.corein, "Available") },
      { Name: "Abagai of the Khergit Borderlands", Subtitle: "Court guest", IsActive: false, ...portraitFields("abagai_portrait", ASSETS.abagai, "Available") }
    ],
    DirectGrantVisible: true,
    GoldGrantVisible: true,
    CanGrantDirect: true,
    CanGrantWithGold: true,
    CanRefuse: true,
    DecisionComplete: false,
    CanContinue: false,
    DirectGrantLabel: "GRANT FOOD",
    GoldGrantLabel: "FUND INSTEAD • 1,240 DENARS",
    RefuseLabel: "REFUSE REQUEST",
    StatusText: "Guildmaster Eronys awaits your immediate answer.",
    PostponeVisible: true,
    CanPostpone: true,
    Transcript: [
      { Speaker: "Guildmaster Eronys", Text: "My liege, our stores cannot carry the villages through the next planting. We ask for relief now.", Role: "npc", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false },
      { Speaker: "You", Text: "How quickly will this reach the villages?", Role: "player", IsNpcLine: false, IsPlayerLine: true, IsSystemLine: false },
      { Speaker: "Guildmaster Eronys", Text: "The first carts can leave today, and the full allotment will be distributed under the town reeve's seal.", Role: "npc", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false }
    ],
    InputText: "",
    ChatScrollVersion: 3,
    CanSend: false,
    ConversationEnabled: true,
    DecisionControlsVisible: true
  },
  "ReignUiCalibrationOverlay.xml": {
    IsLauncherVisible: true,
    IsExpanded: true,
    SelectionVisible: true,
    SelectionX: 525,
    SelectionY: 132,
    SelectionWidth: 610,
    SelectionHeight: 565,
    MovieText: "ReignRoyalCouncilScreen",
    SelectedText: "CouncilTranscriptPanel",
    GeometryText: "X 525 · Y 132 · W 610 · H 565",
    ModeText: "MOVE MODE",
    StatusText: "Select, move, resize, export, and compare against the browser preview."
  },
  "ReignRoyalCouncilScreen.xml": {
    ActionComposerId: "preview-action-composer",
    WarSeat: {
      Role: "war",
      Title: "WAR COUNCILOR",
      Name: "Gwydan",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_war",
      PortraitId: "mengus_portrait",
      PortraitAsset: ASSETS.mengus,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    SpymasterSeat: {
      Role: "spymaster",
      Title: "SPYMASTER",
      Name: "Gwyin",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_spymaster",
      PortraitId: "spymaster_portrait",
      PortraitAsset: ASSETS.abagai,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    EconomicSeat: {
      Role: "economic",
      Title: "ECONOMIC ADVISOR",
      Name: "Elideth",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_economic",
      PortraitId: "adalindis_portrait",
      PortraitAsset: ASSETS.adalindis,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    ForeignSeat: {
      Role: "foreign",
      Title: "FOREIGN ADVISOR",
      Name: "Branara",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_royal_council_foreign",
      PortraitId: "corein_portrait",
      PortraitAsset: ASSETS.corein,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    Transcript: [
      { Speaker: "Gwydan · War Councilor", Text: "No wars threaten the realm. Our field forces remain assembled, and the only recent fighting involved bandits beyond the royal host." },
      { Speaker: "Gwyin · Spymaster", Text: "No intelligence operation is active and no agent is exposed. I have no substantiated rumor requiring Your Majesty's attention." },
      { Speaker: "Elideth · Economic Advisor", Text: "The treasury is stable, supplies are adequate, and no settlement reports a material shortage. Current shipments remain on schedule." },
      { Speaker: "Branara · Foreign Advisor", Text: "Relations with neighboring rulers are steady and political pressure remains low. No foreign conflict presently requires a royal response." }
    ],
    InputText: "",
    CanSend: true,
    IsBusy: false,
    StatusText: "The council awaits your question."
  },
  "ReignWarCouncilScreen.xml": {
    ActionComposerId: "preview-action-composer",
    CanChangeStanding: true, PeaceStandingSelected: false, WarStandingSelected: true,
    PeaceStandingUnselected: true, WarStandingUnselected: false, CanChoosePeaceForce: false,
    PeaceForceText: "Auto v", StandingDropdownVisible: false, PeaceForceOptions: peaceForceOptions(null), PeaceForceLeftOptions: peaceForceOptions(null).slice(0, 6), PeaceForceRightOptions: peaceForceOptions(null).slice(6),
    MapWidth: 820,
    MapHeight: 820,
    MapOffsetX: 0,
    MapOffsetY: 0,
    MarkerRevision: 12,
    SelectedLordIndex: 0,
    LordCount: 4,
    StatusText: "I \u2022 Bevanoc selected. Use the raven to send orders or other instructions.",
    MapImageId: "reigneventart|war_council|map",
    OuterFrameImageId: "reigneventart|war_council|outer_frame",
    RavenImageId: "reigneventart|war_council|raven_scroll",
    PanelFrameImageId: "reigneventart|war_council|panel_frame",
    TallPanelFrameImageId: "reigneventart|war_council|panel_frame_tall",
    PanelFrameOverlayImageId: "reigneventart|war_council|panel_frame_overlay",
    CouncilorName: "Branara",
    CouncilorTitle: "Independent War Councilor",
    CouncilorSkillText: "Tactics 172 \u2022 range 328\nLeadership 141 \u2022 foreign detection 62%\nCapital \u2022 Zeonica",
    CouncilorPortraitId: "corein_portrait",
    CouncilorPortraitArgs: "",
    CouncilorPortraitProvider: "ReignPortraitProvider",
    CouncilorDropdownOpen: false,
    MessengerVisible: false,
    MessengerTitle: "RAVEN TO BEVANOC",
    MessengerInputText: "Hold the western crossing until relieved.",
    MessengerStatus: "",
    Kingdoms: [
      { Name: "Aserai", Strength: "6,657", Detail: "26 clans \u2022 35 parties \u2022 57 settlements", Relation: "PEACE" },
      { Name: "Vlandia", Strength: "6,364", Detail: "27 clans \u2022 35 parties \u2022 52 settlements", Relation: "WAR" },
      { Name: "Northern Empire", Strength: "5,754", Detail: "24 clans \u2022 31 parties \u2022 47 settlements", Relation: "PEACE" },
      { Name: "Battania", Strength: "5,188", Detail: "18 clans \u2022 28 parties \u2022 42 settlements", Relation: "ALLY" }
    ],
    Reports: [
      { Title: "BEVANOC'S PARTY VS WESTERN ARMY", Result: "Victory \u2022 114–76 troops \u2022 losses 8/31", Captures: "Lord Aldric captured", PlayerRealmInvolved: true, OrdinaryBattle: false },
      { Title: "CARAVAN OF SARGOT VS LOOTERS", Result: "Defeat \u2022 24–38 troops \u2022 losses 24/11", Captures: "No lord captured", PlayerRealmInvolved: false, OrdinaryBattle: true },
      { Title: "BRANARA'S PARTY VS DESERTERS", Result: "Victory \u2022 83–53 troops \u2022 losses 9/33", Captures: "No lord captured", PlayerRealmInvolved: true, OrdinaryBattle: false }
    ],
    Settlements: [
      { TypeLabel: "TOWN", Name: "Amitatys" },
      { TypeLabel: "CASTLE", Name: "Ataconia Castle" },
      { TypeLabel: "VILLAGE", Name: "Ayn Assadi" },
      { TypeLabel: "TOWN", Name: "Baltakhand" },
      { TypeLabel: "CASTLE", Name: "Chanopsis Castle" },
      { TypeLabel: "VILLAGE", Name: "Dvorusta" },
      { TypeLabel: "TOWN", Name: "Epicrotea" },
      { TypeLabel: "TOWN", Name: "Zeonica" }
    ],
    Lords: [
      { Roman: "I", Name: "Bevanoc", ClanName: "Caerder", PartyText: "133 \u2022 I 19  M 114  C 0", StatusText: "Following Orders", PortraitId: "mengus_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: false, IsSelected: true },
      { Roman: "II", Name: "Bevanwyn", ClanName: "Caerder", PartyText: "166 \u2022 I 33  M 120  C 13", StatusText: "Following Orders", PortraitId: "aeron_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: false, IsSelected: false },
      { Roman: "III", Name: "Branara", ClanName: "Caerder", PartyText: "124 \u2022 I 12  M 110  C 2", StatusText: "Free Roam", PortraitId: "corein_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: false, IsSelected: false },
      { Roman: "—", Name: "Llewara", ClanName: "Caerder", PartyText: "No active party", StatusText: "Free Roam", PortraitId: "ira_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", RavenImageId: "reigneventart|war_council|raven_scroll", CanMobilize: true, IsSelected: false }
    ],
    CouncilorCandidates: [
      { Name: "Bevanoc", CouncilorDetail: "Tactics 119 \u2022 Leadership 134", PortraitId: "mengus_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", IsCouncilor: false },
      { Name: "Branara", CouncilorDetail: "Tactics 172 \u2022 Leadership 141", PortraitId: "corein_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", IsCouncilor: true },
      { Name: "Llewara", CouncilorDetail: "Tactics 86 \u2022 Leadership 109", PortraitId: "ira_portrait", PortraitArgs: "", PortraitProvider: "ReignPortraitProvider", IsCouncilor: false }
    ]
  },
  "ReignSpymasterScreen.xml": {
    SpymasterName: "Gwyin",
    PortraitCacheKey: "preview_spymaster_abagai",
    PortraitId: "spymaster_portrait",
    PortraitAsset: ASSETS.spymasterFullBody,
    PortraitArgs: "",
    PortraitProvider: "ReignPortraitProvider",
    HasPortrait: true,
    HasSpymaster: true,
    ShowSpymasterAiPortrait: true,
    ShowSpymasterTableauFallback: false,
    ShowAppointmentPrompt: false,
    AppointmentPending: false,
    SpymasterModel: {
      HasSpymaster: true,
      BannerCodeText: "",
      BodyProperties: "",
      CharStringId: "lord_6_12",
      EquipmentCode: "",
      IsFemale: true,
      MountCreationKey: "",
      StanceIndex: 0,
      ArmorColor1: "#473421FF",
      ArmorColor2: "#8C733DFF",
      Race: 0,
      IsTableauEnabled: false
    },
    IsIntelligence: true,
    IsSubterfuge: false,
    IsReputation: false,
    IsArchive: false,
    ShowOperationPanel: true,
    IsLands: true,
    IsPeople: false,
    ShowPeopleGroups: false,
    ShowSocialItem: false,
    PeopleGroupText: "OWN NOBLES",
    SocialScopeText: "YOUR REPUTATION",
    TargetName: "Ab Comer Castle",
    TargetDetail: "A stronghold held by foreign forces.",
    ActionName: "Gather settlement intelligence",
    OperationDetail: "Appoint a Spymaster and choose an operation.",
    AssessmentText: "UNKNOWN",
    IsOrdinaryAssessment: true,
    IsChallengingAssessment: false,
    SocialItemName: "Select rumor or reputation",
    TargetDropdownOpen: false,
    ActionDropdownOpen: false,
    SocialDropdownOpen: false,
    CanBegin: false,
    CanBreakout: false,
    BreakoutText: "",
    StatusText: "",
    ReportText: "No intelligence operations have been recorded.",
    TargetOptions: [
      { Id: "castle_A1", Label: "Ab Comer Castle", Detail: "A stronghold held by foreign forces.", IsSelected: true },
      { Id: "town_V6", Label: "Pravend", Detail: "A stronghold within your realm.", IsSelected: false },
      { Id: "town_K1", Label: "Marunath", Detail: "A stronghold held by foreign forces.", IsSelected: false }
    ],
    ActionOptions: [
      { Id: "land_intelligence", Label: "Gather settlement intelligence", Detail: "", IsSelected: true },
      { Id: "counterintelligence", Label: "Counterintelligence sweep", Detail: "", IsSelected: false }
    ],
    SocialItems: [],
    Reports: []
  },
  "ReignCourtEconomicReportScreen.xml": {
    SourceName: "Pravend",
    TargetName: "Sargot",
    GoodsName: "FOOD SUPPLY",
    AvailableText: "600",
    AmountText: "100",
    ArrivalText: "ARRIVES IN: 2.4 DAYS",
    Notes: "Winter relief for the western garrison.",
    IsSourceDropdownOpen: false,
    IsTargetDropdownOpen: true,
    SourceOptions: [
      { StringId: "town_V6", Name: "Pravend", DetailText: "Town - Vlandia" },
      { StringId: "town_V1", Name: "Sargot", DetailText: "Town - Vlandia" },
      { StringId: "town_V4", Name: "Jaculan", DetailText: "Town - Vlandia" }
    ],
    TargetOptions: [
      { StringId: "town_V6", Name: "Pravend", DetailText: "Town - Vlandia" },
      { StringId: "town_V1", Name: "Sargot", DetailText: "Town - Vlandia" },
      { StringId: "town_A1", Name: "Ortysia", DetailText: "Town - Western Empire" },
      { StringId: "castle_V2", Name: "Talivel Castle", DetailText: "Castle - Vlandia" },
      { StringId: "town_K1", Name: "Marunath", DetailText: "Town - Battania" },
      { StringId: "town_B1", Name: "Car Banseth", DetailText: "Town - Battania" },
      { StringId: "town_S1", Name: "Varnovapol", DetailText: "Town - Sturgia" }
    ],
    ReportDateText: "16th of Summer, 1092",
    StatusText: "",
    TreasuryText: "38,742",
    DailyIncomeText: "+4,582",
    DailyExpensesText: "-3,271",
    NetDailyChangeText: "+1,311",
    TariffsTradeText: "1,842",
    VillageManorText: "2,154",
    TradeBalanceText: "-1,126",
    OutstandingDebtsText: "2,390",
    GrainTotalText: "14,809",
    MeatTotalText: "6,320",
    TimberTotalText: "8,700",
    IronTotalText: "4,210",
    TotalMilitiaText: "1,380",
    TotalGarrisonText: "920",
    GarrisonUpkeepText: "1,682",
    WarInventoryText: "Adequate",
    OverallStabilityText: "73",
    AverageLoyaltyText: "73",
    AverageSecurityText: "66",
    CorruptionRiskText: "Low",
    NetDailyPositive: true,
    NetDailyNegative: false,
    TradeBalancePositive: false,
    TradeBalanceNegative: true,
    CanSubmit: true,
    HasSettlementOverflow: true,
    EconomicAdvisor: {
      Role: "economic",
      Title: "ECONOMIC ADVISOR",
      Name: "Elideth",
      Status: "AVAILABLE",
      IsAvailable: true,
      HasPortrait: true,
      PortraitCacheKey: "preview_economic_report_advisor",
      PortraitId: "adalindis_portrait",
      PortraitAsset: ASSETS.adalindis,
      PortraitAdditionalArgs: "",
      PortraitTextureProviderName: "ReignPortraitProvider"
    },
    Settlements: [
      { Name: "Pravend", GovernorName: "Eadfrid of the Oak", ProsperityText: "78", FoodText: "82", FoodHint: { HintText: "Reserves: 82 / 200; projected in seven days: 54. Village hearths: 300; agricultural recovery: 75%; actual / potential food: 6.75 / 9.00." }, SecurityText: "76", LoyaltyText: "88", MilitiaText: "330", GarrisonText: "210", GarrisonFoodText: "120", GarrisonWageText: "420", WageHint: { HintText: "Garrison wages: 420 per day; clan military wages: 1,682; seven-day reserve: 11,774; recruitment liabilities: 340." }, GrainText: "1,210", FishText: "680", MeatText: "420", OlivesText: "360", BeerText: "240", ButterText: "220", GrapesText: "310", DatesText: "180" },
      { Name: "Sargot", GovernorName: "Lanthos of Vostrum", ProsperityText: "64", FoodText: "58", FoodHint: { HintText: "Reserves: 82 / 200; projected in seven days: 54. Village hearths: 300; agricultural recovery: 75%; actual / potential food: 6.75 / 9.00." }, SecurityText: "62", LoyaltyText: "70", MilitiaText: "270", GarrisonText: "180", GarrisonFoodText: "100", GarrisonWageText: "360", WageHint: { HintText: "Garrison wages: 420 per day; clan military wages: 1,682; seven-day reserve: 11,774; recruitment liabilities: 340." }, GrainText: "1,110", FishText: "410", MeatText: "380", OlivesText: "320", BeerText: "200", ButterText: "190", GrapesText: "280", DatesText: "150" },
      { Name: "Ocs Hall", GovernorName: "Dionor of Ocs", ProsperityText: "72", FoodText: "74", FoodHint: { HintText: "Reserves: 82 / 200; projected in seven days: 54. Village hearths: 300; agricultural recovery: 75%; actual / potential food: 6.75 / 9.00." }, SecurityText: "70", LoyaltyText: "75", MilitiaText: "290", GarrisonText: "190", GarrisonFoodText: "110", GarrisonWageText: "400", WageHint: { HintText: "Garrison wages: 420 per day; clan military wages: 1,682; seven-day reserve: 11,774; recruitment liabilities: 340." }, GrainText: "1,260", FishText: "560", MeatText: "330", OlivesText: "330", BeerText: "210", ButterText: "200", GrapesText: "330", DatesText: "170" },
      { Name: "Jaculan", GovernorName: "Meritor of Jaculan", ProsperityText: "56", FoodText: "65", FoodHint: { HintText: "Reserves: 82 / 200; projected in seven days: 54. Village hearths: 300; agricultural recovery: 75%; actual / potential food: 6.75 / 9.00." }, SecurityText: "50", LoyaltyText: "60", MilitiaText: "290", GarrisonText: "160", GarrisonFoodText: "110", GarrisonWageText: "320", WageHint: { HintText: "Garrison wages: 420 per day; clan military wages: 1,682; seven-day reserve: 11,774; recruitment liabilities: 340." }, GrainText: "960", FishText: "330", MeatText: "340", OlivesText: "240", BeerText: "160", ButterText: "160", GrapesText: "260", DatesText: "120" },
      { Name: "Charas", GovernorName: "Belithor of Charas", ProsperityText: "68", FoodText: "71", FoodHint: { HintText: "Reserves: 82 / 200; projected in seven days: 54. Village hearths: 300; agricultural recovery: 75%; actual / potential food: 6.75 / 9.00." }, SecurityText: "74", LoyaltyText: "68", MilitiaText: "260", GarrisonText: "170", GarrisonFoodText: "110", GarrisonWageText: "340", WageHint: { HintText: "Garrison wages: 420 per day; clan military wages: 1,682; seven-day reserve: 11,774; recruitment liabilities: 340." }, GrainText: "1,090", FishText: "440", MeatText: "280", OlivesText: "260", BeerText: "180", ButterText: "170", GrapesText: "240", DatesText: "140" }
    ],
    SurplusRows: [
      { SettlementName: "Pravend", ItemName: "Grain", AmountText: "+1,210", IsSurplus: true, IsShortage: false },
      { SettlementName: "Ocs Hall", ItemName: "Fish", AmountText: "+560", IsSurplus: true, IsShortage: false },
      { SettlementName: "Charas", ItemName: "Olives", AmountText: "+260", IsSurplus: true, IsShortage: false },
      { SettlementName: "Sargot", ItemName: "Meat", AmountText: "-380", IsSurplus: false, IsShortage: true },
      { SettlementName: "Jaculan", ItemName: "Grain", AmountText: "-960", IsSurplus: false, IsShortage: true }
    ]
  },
  "ReignPartyChatScreen.xml": {
    ShowLifeControls: true, ShowStandardComposer: false, ComposerWidth: 890, SendButtonLeft: 1084, SendButtonWidth: 240,
    CanAnnounce: true, InputText: "We leave at dawn. Gather by the eastern gate.", SendLabel: "Send",
    IsHomesMode: false, IsHomeDirectory: false, HasChatSession: true, AllowGroupSelection: true,
    RosterTitle: "Available Characters", ClearSelectionText: "Clear", HomeDirectoryText: "", InputEnabled: true,
    Title: "Council Before the Lords of Calradia",
    LocationText: "Great Hall of Amitatys",
    SelectedSummary: "3 characters selected · 2 active · 7 available",
    BusyText: "",
    PartyMembers: SAMPLE_DATA.PartyMembers.map((member, index) => ({
      ...member,
      CanVisitInPerson: false, DisplayStatus: member.Status, StatusTop: 65, StatusHeight: 28,
      IsActive: index < 2,
      IsInactive: index >= 2
    })),
    ChatLines: [
      {
        Speaker: "Mengus fen Gruffendoc",
        Text: "The western clans will support the grain road, but only if every promised banner is named before the council adjourns.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      },
      {
        Speaker: "Aeric fen Seanel",
        Text: "Then record my oath first. My riders will hold the western pass until the final wagon reaches Marunath.",
        IsPlayerLine: true,
        IsNpcLine: false,
        IsSystemLine: false
      },
      {
        Speaker: "Council Herald",
        Text: "Corein, Daughter of High King Caladog, has joined the party conversation.",
        IsPlayerLine: false,
        IsNpcLine: false,
        IsSystemLine: true
      },
      {
        Speaker: "Corein, Daughter of High King Caladog",
        Text: "Long names and longer promises are easy. Tell us how many companies can actually ride before the first snow.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      }
    ]
  },
  "ReignCastleChatScreen.xml": {
    CastleRoomName: "Castle Gardens",
    HasCastleArt: true,
    CastleArtImageId: "feast_empire_toasts_and_table_talk",
    CastleTavernArtImageId: "reigntavern|scene|empire|tavern_01.jpg",
    ShowCastleTavernArt: false,
    ShowCastleApprovedFallback: false,
    EventImageAsset: ASSETS.eventArt,
    PlayerDisplayName: "Fercread\nRuler",
    PlayerPortraitId: "player_portrait",
    PlayerPortraitAdditionalArgs: "",
    PlayerPortraitTextureProviderName: "ReignPortraitProvider",
    PlayerPortraitCacheKey: "preview_castle_chat_player",
    IsBusy: true,
    BusyText: "Waiting for Ceryniana to speak... (3 of 4)",
    InputEnabled: false,
    PortraitPreviewCacheKey: "preview_castle_chat_portrait",
    PartyMembers: [
      { Name: "Zotyra", HasNativePortrait: true, IsChild: false, ...portraitFields("zotyra_portrait", ASSETS.ira, "Present") },
      { Name: "Othessa", HasNativePortrait: true, IsChild: false, ...portraitFields("othessa_portrait", ASSETS.corein, "Present") },
      { Name: "Ceryniana", HasNativePortrait: true, IsChild: false, ...portraitFields("ceryniana_portrait", ASSETS.adalindis, "Speaking") },
      { Name: "Seorgys", HasNativePortrait: true, IsChild: false, ...portraitFields("seorgys_portrait", ASSETS.mengus, "Waiting") }
    ],
    ChatLines: [
      {
        Speaker: "Zotyra",
        Text: "The roses have taken well to the southern wall, though the household still argues over who deserves the credit.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      },
      {
        Speaker: "Othessa",
        Text: "Credit matters less than whether the garden remains a place where difficult truths can be spoken without an audience gathering at the doors.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      }
    ]
  },
  "ReignIndividualChatScreen.xml": {
    ChatLines: INDIVIDUAL_CHAT_LINES,
    LocationText: "Location: Marunath",
    BusyText: "",
    PlayerPortraitCropImageWidth: 241.8,
    PlayerPortraitCropImageHeight: 322.4,
    NpcPortraitCropImageWidth: 241.8,
    NpcPortraitCropImageHeight: 322.4,
    ZoomPortraitImageWidth: 540,
    ZoomPortraitImageHeight: 720,
    ZoomFrameWidth: 576,
    ZoomFrameHeight: 756
  },
  "ReignCorrespondenceScreen.xml": {
    CanOpenMarriageOffer: false,
    ChatLines: [
      {
        Speaker: "Mengus fen Gruffendoc · Day 184 (delivered)",
        Text: "The western clans have accepted your proposed winter grain escort, but they expect proof that every promised banner will actually arrive.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      },
      {
        Speaker: "Aeric fen Seanel · Day 185 (in transit)",
        Text: "Then tell them the first wagons leave within seven days. I will ride with the escort until it clears the western pass.",
        IsPlayerLine: true,
        IsNpcLine: false,
        IsSystemLine: false
      },
      {
        Speaker: "Mengus fen Gruffendoc · Day 186 (read)",
        Text: "That answer will satisfy most of them. Caladog will still demand names, numbers, and the road each company is sworn to defend.",
        IsPlayerLine: false,
        IsNpcLine: true,
        IsSystemLine: false
      }
    ],
    BusyText: ""
  }
};

function getTavernHouseSampleData(preset) { return { ...getTavernHouseBaseData(preset), ActionComposerId: "preview-action-composer" }; }

const PREFAB_ROOT_KEYS = {
  "ReignActionBuilder.xml": ["Title", "Recipient", "Sentence", "Error", "Search", "Entry", "EntryVisible", "SearchVisible", "CanAccept", "CanBack", "Choices"],
  "ReignTavernHouseScreen.xml": ["ActionComposerId", ...TAVERN_HOUSE_ROOT_KEYS],
  "ReignClanAccordsScreen.xml": CLAN_ACCORDS_ROOT_KEYS,
  "AIPortraitsMemoriesBook.xml": ["MemoryItems", "MemoryBookImageId", "TitleText", "CaptionText", "CounterText", "HasMemories"],
  "ReignAmbassadorScreen.xml": ["PlayerBannerId", "PlayerBannerAsset", "PlayerBannerArgs", "PlayerBannerProvider", "ForeignAdvisor", "Ambassadors", "ShowEmptyState", "StatusText"],
  "ReignCastleChatScreen.xml": ["View", "ActionComposerId", "CastleRoomName", "HasCastleArt", "CastleArtImageId", "CastleTavernArtImageId", "ShowCastleTavernArt", "ShowCastleApprovedFallback", "EventImageAsset", "PlayerDisplayName", "PlayerPortraitId", "PlayerPortraitAdditionalArgs", "PlayerPortraitTextureProviderName", "PlayerPortraitCacheKey", "IsBusy", "BusyText", "InputEnabled", "InputText", "PartyMembers", "ChatLines", "ChatScrollVersion", "IsOverlayVisible", "IsPortraitPreviewVisible", "PortraitPreviewId", "PortraitPreviewAsset", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewCacheKey", "PortraitPreviewName"],
  "ReignCastleLayoutScreen.xml": ["CastleMapImageId", "GovernmentButtonText", "CastleGardenSlots", "TrainingYardSlots", "StableCourtyardSlots", "GuestBedroomRow1", "GuestBedroomRow2", "GuestBedroomRow3", "GuestBedroomRow4", "NobleSolarSlots", "InnerCourtyardSlots", "MainHallSlots", "RoyalBedroomSlots", "ThroneRoomSlots", "LibrarySlots", "DiningChamberSlots", "PortraitGallerySlots", "ChapelSlots", "BathsSlots", "BattlementSlots"],
  "ReignCorrespondenceScreen.xml": ["ActionComposerId", "Contacts", "SelectedName", "SelectedSubtitle", "ChatLines", "ChatScrollVersion", "BusyText", "InputText"],
  "ReignCourtEconomicReportScreen.xml": ["SourceName", "TargetName", "GoodsName", "AvailableText", "AmountText", "ArrivalText", "Notes", "IsSourceDropdownOpen", "IsTargetDropdownOpen", "SourceOptions", "TargetOptions", "ReportDateText", "StatusText", "TreasuryText", "DailyIncomeText", "DailyExpensesText", "NetDailyChangeText", "TariffsTradeText", "VillageManorText", "TradeBalanceText", "OutstandingDebtsText", "GrainTotalText", "MeatTotalText", "TimberTotalText", "IronTotalText", "TotalMilitiaText", "TotalGarrisonText", "GarrisonUpkeepText", "WarInventoryText", "NetDailyPositive", "NetDailyNegative", "TradeBalancePositive", "TradeBalanceNegative", "CanSubmit", "HasSettlementOverflow", "EconomicAdvisor", "Settlements", "SurplusRows"],
  "ReignCourtScreen.xml": ["CourtWorkspaceTopInset", "CourtTitle", "DocketTitle", "PlayerName", "PlayerBannerId", "PlayerBannerAsset", "PlayerBannerArgs", "PlayerBannerProvider", "HomeVisible", "LargeHomeVisible", "ReferenceHomeVisible", "TabContentVisible", "DetailVisible", "AudienceVisible", "ActionsVisible", "ScreenTitle", "ScreenSubtitle", "StatusText", "DetailTitle", "DetailBody", "DetailMeta", "AudienceSpeaker", "AudienceText", "AudienceEmotion", "AudienceInput", "AudienceHasPortrait", "AudiencePortraitId", "AudiencePortraitAsset", "AudiencePortraitAdditionalArgs", "AudiencePortraitTextureProviderName", "AudiencePortraitCacheKey", "Tabs", "Counters", "Lands", "DailyAgenda", "DocketItemCount", "DocketScrollVisible", "People", "DiplomaticStatus", "AmbassadorCards", "Rumors", "TabRows", "Actions", "RoyalCommandsVisible", "LocalModeNoticeVisible", "HasChancellor", "ChancellorName", "ChancellorPortraitCacheKey", "ChancellorStateText", "ChancellorSalaryText", "ChancellorToggleVisible"],
  "ReignCourtPetitionScreen.xml": ["ActionComposerId", "Title", "PetitionerName", "PetitionerRole", "SeverityText", "ProblemText", "RequestTypeText", "RequestedResourceText", "PurposeText", "NeedFacts", "RequestTerms", "BenefitText", "DangerText", "WarningText", "EventImageId", "EventImageAsset", "ActiveParticipants", "AvailableAttendees", "DirectGrantVisible", "GoldGrantVisible", "CanGrantDirect", "CanGrantWithGold", "CanRefuse", "DecisionComplete", "DecisionControlsVisible", "PostponeVisible", "CanPostpone", "CanContinue", "DirectGrantLabel", "GoldGrantLabel", "RefuseLabel", "StatusText", "Transcript", "InputText", "ChatScrollVersion", "CanSend", "ConversationEnabled"],
  "ReignDiplomacyAnnouncementScreen.xml": ["Title", "DateText", "ShowTarget", "Outcome", "Summary", "Terms", "ReportText", "ActorName", "ActorKingdom", "ActorReason", "ActorPortraitId", "ActorPortraitAsset", "ActorPortraitArgs", "ActorPortraitProvider", "ActorPortraitCacheKey", "ActorBannerId", "ActorBannerArgs", "ActorBannerProvider", "TargetName", "TargetKingdom", "TargetReason", "TargetPortraitId", "TargetPortraitAsset", "TargetPortraitArgs", "TargetPortraitProvider", "TargetPortraitCacheKey", "TargetBannerId", "TargetBannerArgs", "TargetBannerProvider"],
  "ReignFamilyChambersScreen.xml": ["Title", "Subtitle", "Adults", "Children", "SelectionText", "StatusText", "IsBusy", "CanEnterScene"],
  "ReignTrainingYardScreen.xml": ["Title", "TrainerHeading", "TroopHeading", "SelectedTrainerName", "SelectedTrainerDetail", "LeadershipText", "WeaponText", "RateText", "ElapsedText", "DeliveredText", "StatusText", "IsTraining", "CanTrain", "CanStop", "HasSelectedTrainer", "HasSelectedGeneratedPortrait", "SelectedPortraitCacheKey", "SelectedPortraitId", "SelectedPortraitAdditionalArgs", "SelectedPortraitTextureProviderName", "PortraitAsset", "TrainerCount", "TroopCount", "Trainers", "Troops"],
  "ReignGovernmentScreen.xml": GOVERNMENT_ROOT_KEYS,
  "ReignIndividualChatScreen.xml": ["View", "ActionComposerId", "CanChangeOutfit", "IsOutfitVisible", "IsOutfitInputVisible", "IsOutfitComparisonVisible", "IsOutfitReady", "IsOutfitChanging", "CanCancelOutfit", "OutfitDescription", "OutfitStatus", "OutfitBeforePath", "OutfitAfterPath", "OutfitBeforeWidth", "OutfitBeforeHeight", "OutfitAfterWidth", "OutfitAfterHeight", "PlayerName", "PlayerSubtitle", "PlayerInfo", "PlayerPortraitId", "PlayerPortraitAsset", "PlayerPortraitAdditionalArgs", "PlayerPortraitTextureProviderName", "PlayerPortraitCropImageWidth", "PlayerPortraitCropImageHeight", "NpcName", "NpcSubtitle", "NpcInfo", "NpcPortraitId", "NpcPortraitAsset", "NpcPortraitAdditionalArgs", "NpcPortraitTextureProviderName", "NpcPortraitCropImageWidth", "NpcPortraitCropImageHeight", "LocationText", "IsBusy", "BusyText", "InputEnabled", "InputText", "SendButtonText", "ChatLines", "ChatScrollVersion", "IsPortraitZoomVisible", "IsPregnancyWarningVisible", "ZoomFrameWidth", "ZoomFrameHeight", "ZoomPortraitImageWidth", "ZoomPortraitImageHeight", "ZoomPortraitId", "ZoomPortraitAsset", "ZoomPortraitAdditionalArgs", "ZoomPortraitTextureProviderName"],
  "ReignPortraitRefreshPopup.xml": ["Title", "Detail", "LookAgainText", "ReflectText", "CancelText", "CanReflect"],
  "ReignPlayerApproachPopup.xml": ["Title", "Detail", "AcceptText", "DeclineText"],
  "ReignBanterPanel.xml": ["Title", "Collapsed", "Expanded", "PanelHeight", "CollapseText", "CanJoin", "ChatScrollVersion", "Status", "Transcript"],
  "ReignNotableGenerationPopup.xml": ["Title", "Detail", "Progress", "HasError"],
  "ReignPartyChatScreen.xml": ["ShowPartyContinuation", "CanContinuePartyChat", "PartyContinuationUnavailable", "View", "ActionComposerId", "Title", "LocationText", "SelectedSummary", "BusyText", "InputText", "ChatLines", "PartyMembers", "IsOverlayVisible", "IsPortraitPreviewVisible", "PortraitPreviewId", "PortraitPreviewAsset", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewName"],
  "ReignRoyalCouncilScreen.xml": ["ActionComposerId", "WarSeat", "SpymasterSeat", "EconomicSeat", "ForeignSeat", "Transcript", "InputText", "CanSend", "IsBusy", "StatusText"],
  "ReignSocialEventScreen.xml": ["ActionComposerId", "IsOverlayVisible", "ShowPhaseControls", "ApproachLabel", "NextPhaseLabel", "LeaveEventLabel", "ShowWildernessArt", "EventTitle", "PhaseTitle", "PhaseStatus", "PhaseDescription", "EventImageId", "EventImageAsset", "EventImageText", "EventArtSize", "EventArtRowHeight", "AttendeeListTopMargin", "AttendeeListBottomMargin", "AttendeeTitleTopMargin", "EventArtTopMargin", "EventBackgroundImageId", "BackgroundImageId", "BackgroundAsset", "BackgroundAdditionalArgs", "BackgroundTextureProviderName", "CultureTint", "BusyText", "InputText", "ChatLines", "ChatScrollVersion", "ActiveParticipants", "AvailableAttendees", "IsPortraitPreviewVisible", "PortraitPreviewFrameWidth", "PortraitPreviewFrameHeight", "PortraitPreviewImageWidth", "PortraitPreviewImageHeight", "PortraitPreviewId", "PortraitPreviewAsset", "PortraitPreviewAdditionalArgs", "PortraitPreviewTextureProviderName", "PortraitPreviewName"],
  "ReignSpymasterScreen.xml": ["SpymasterName", "PortraitCacheKey", "PortraitId", "PortraitArgs", "PortraitProvider", "HasPortrait", "HasSpymaster", "ShowSpymasterAiPortrait", "ShowSpymasterTableauFallback", "ShowAppointmentPrompt", "AppointmentPending", "SpymasterModel", "IsIntelligence", "IsSubterfuge", "IsReputation", "IsArchive", "ShowOperationPanel", "IsLands", "IsPeople", "ShowPeopleGroups", "ShowSocialItem", "PeopleGroupText", "SocialScopeText", "TargetName", "TargetDetail", "ActionName", "OperationDetail", "AssessmentText", "IsOrdinaryAssessment", "IsChallengingAssessment", "SocialItemName", "TargetDropdownOpen", "ActionDropdownOpen", "SocialDropdownOpen", "CanBegin", "CanBreakout", "BreakoutText", "StatusText", "ReportText", "TargetOptions", "ActionOptions", "SocialItems", "Reports"],
  "ReignUiCalibrationOverlay.xml": ["IsLauncherVisible", "IsExpanded", "SelectionVisible", "SelectionX", "SelectionY", "SelectionWidth", "SelectionHeight", "MovieText", "SelectedText", "GeometryText", "ModeText", "StatusText"],
  "ReignWarCouncilScreen.xml": ["CanChangeStanding", "PeaceStandingSelected", "WarStandingSelected", "PeaceStandingUnselected", "WarStandingUnselected", "CanChoosePeaceForce", "PeaceForceText", "StandingDropdownVisible", "PeaceForceOptions", "PeaceForceLeftOptions", "PeaceForceRightOptions", "ActionComposerId", "MapWidth", "MapHeight", "MapOffsetX", "MapOffsetY", "MarkerRevision", "SelectedLordIndex", "LordCount", "StatusText", "MapImageId", "OuterFrameImageId", "RavenImageId", "PanelFrameImageId", "TallPanelFrameImageId", "PanelFrameOverlayImageId", "CouncilorName", "CouncilorTitle", "CouncilorSkillText", "CouncilorPortraitId", "CouncilorPortraitArgs", "CouncilorPortraitProvider", "CouncilorDropdownOpen", "MessengerVisible", "MessengerTitle", "MessengerInputText", "MessengerStatus", "Kingdoms", "Reports", "Settlements", "Lords", "CouncilorCandidates"]
};

export function getNativeAugmentationSampleData(target) {
  const skillXpHint = {
    HintText: "Current experience and progress toward the next skill level.",
    ReignIsCurrentHeroSkillVisible: true
  };
  const learningLimitTooltip = {
    HintText: "The learning limit derived from attributes and focus.",
    ReignIsSkillValueVisible: true
  };
  const learningRateTooltip = {
    HintText: "The current experience multiplier for this skill.",
    ReignIsSkillValueVisible: true
  };
  const currentSkill = {
    LearningRate: 4.25,
    CurrentSkillXP: 3480,
    XpRequiredForNextLevel: 4200,
    ReignSkillLevelText: "124",
    ReignSkillProgressText: "3,480 / 4,200",
    ReignLearningRateText: "Learning rate 4.25x",
    ReignIsSkillValueVisible: true,
    ReignIsCurrentHeroSkillVisible: true,
    CanLearnSkill: true,
    SkillXPHint: { ...skillXpHint },
    LearningLimitTooltip: { ...learningLimitTooltip },
    LearningRateTooltip: { ...learningRateTooltip }
  };
  const marriageMember = {
    Relation: 18,
    ReignIsRelationVisible: false,
    ReignIsRelationHidden: true
  };
  return {
    IsSelected: true,
    IsAnyValidMemberSelected: true,
    IsReignTavernArtVisible: true,
    ReignTavernSceneImageId: "reigntavern|scene|empire|preview",
    ReignTavernFrameImageId: "reigntavern|frame",
    ReignSkillValueText: "124",
    ReignSkillLevelText: "124",
    ReignSkillProgressText: "3,480 / 4,200",
    ReignLearningRateText: "Learning rate 4.25x",
    ReignSmithySkillLevelText: "124",
    ReignCraftingSkillText: "124",
    ReignRelationText: "?",
    ReignIsRelationVisible: false,
    ReignIsRelationHidden: true,
    ReignIsSkillValueVisible: true,
    ReignIsSkillValueHidden: false,
    ReignIsCurrentHeroSkillVisible: true,
    ReignIsCraftingSkillVisible: true,
    ReignIsCraftingSkillHidden: false,
    CanLearnSkill: true,
    HasSkillValueIncreasedInCurrentStage: true,
    CurrentSkill: currentSkill,
    LearningLimitTooltip: learningLimitTooltip,
    LearningRateTooltip: learningRateTooltip,
    OffereeClanMember: { ...marriageMember },
    OffererClanMember: { ...marriageMember },
    IsReignPortraitAvailable: true,
    ReignPortraitCacheKey: "preview_native_party_portrait",
    PortraitAsset: ASSETS.mengus,
    Banner_9: { AdditionalArgs: "", Id: "player_clan_banner", TextureProviderName: "BannerTableau", BannerAsset: ASSETS.courtPlayerBanner },
    IsConversationPortraitVisible: true,
    IsPlayerPortraitVisible: true,
    IsAIInfluenceMemoryVisible: true,
    IsPortraitZoomVisible: false,
    NpcPortraitCropImageWidth: 188,
    NpcPortraitCropImageHeight: 250.6667,
    ConversationPortraitTextureProviderName: "ReignPortraitProvider",
    ConversationPortraitAdditionalArgs: "",
    ConversationPortraitId: "mengus_portrait",
    ConversationPortraitAsset: ASSETS.mengus,
    PlayerPortraitCropImageWidth: 188,
    PlayerPortraitCropImageHeight: 250.6667,
    PlayerPortraitTextureProviderName: "ReignPortraitProvider",
    PlayerPortraitAdditionalArgs: "",
    PlayerPortraitId: "adalindis_portrait",
    PlayerPortraitAsset: ASSETS.adalindis,
    AIInfluenceMemoryFrameWidth: 746,
    AIInfluenceMemoryFrameHeight: 566,
    AIInfluenceMemoryImageWidth: 720,
    AIInfluenceMemoryImageHeight: 540,
    AIInfluenceMemoryTextureProviderName: "ReignPortraitProvider",
    AIInfluenceMemoryAdditionalArgs: "",
    AIInfluenceMemoryId: "conversation_memory_scene",
    AIInfluenceMemoryAsset: ASSETS.eventArt,
    ZoomFrameWidth: 726,
    ZoomFrameHeight: 956,
    ZoomPortraitImageWidth: 690,
    ZoomPortraitImageHeight: 920,
    ZoomPortraitTextureProviderName: "ReignPortraitProvider",
    ZoomPortraitAdditionalArgs: "",
    ZoomPortraitId: "mengus_portrait",
    ZoomPortraitAsset: ASSETS.mengus,
    $nativeAugmentationTarget: target
  };
}

function courtLifePreview(source, title, participants, reply, terms, severity = "AT COURT", overrides = {}) {
  return {
    Title: `${source} AUDIENCE`, PetitionerName: title, PetitionerRole: source, SeverityText: severity,
    ProblemText: terms, RequestTypeText: source, RequestedResourceText: terms, PurposeText: "A personal audience with the ruler",
    NeedFacts: participants.map((person) => person.Name).join(" • "), RequestTerms: terms, BenefitText: "", DangerText: "", WarningText: "",
    HasPortrait: Boolean(participants[0]?.HasPortrait), PortraitId: participants[0]?.PortraitId || "", PortraitAsset: participants[0]?.PortraitAsset || "",
    ActiveParticipants: participants.map((person) => ({ PortraitAdditionalArgs: "", PortraitTextureProviderName: "ReignPortraitProvider",
      ...person, IsActive: true, Status: person.Subtitle })), AvailableAttendees: [],
    DirectGrantVisible: true, GoldGrantVisible: true, CanGrantDirect: true, CanGrantWithGold: false, CanRefuse: true,
    DecisionComplete: false, CanContinue: false, DirectGrantLabel: "REVIEW DECISIONS", GoldGrantLabel: "CONFIRM", RefuseLabel: "END AUDIENCE",
    StatusText: "Your guests await your response.", PostponeVisible: true, CanPostpone: true, InputText: "Tell me more.", CanSend: true,
    ConversationEnabled: true, DecisionControlsVisible: true, ChatScrollVersion: 2,
    Transcript: [{ Speaker: participants[0].Name, Text: reply, Role: "assistant", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false },
      { Speaker: "You", Text: "Thank you for coming. Tell me more.", Role: "user", IsNpcLine: false, IsPlayerLine: true, IsSystemLine: false }],
    ...overrides
  };
}

export function getSampleDataForPrefab(fileName, preset = "") {
  const normalizedName = String(fileName || "").replaceAll("\\", "/").split("/").at(-1);
  if (normalizedName === "ReignActionBuilder.xml") {
    const result = structuredClone(SAMPLE_DATA[normalizedName]);
    const choices = labels => labels.map(Label => ({ Label, Detail: "", Enabled: true }));
    if (preset) result.CanBack = true;
    if (preset === "action-builder-categories") Object.assign(result, {
      Title: "Choose an action", SearchVisible: true,
      Choices: choices(["Give…", "Go to…", "Support…", "Government decisions…", "Court and intelligence…", "Party and military orders…", "Diplomacy…", "Clan and kingdom…", "Personal requests…"])
    });
    if (preset === "action-builder-selection") Object.assign(result, {
      Title: "Select troops", SearchVisible: true, CanAccept: false,
      Choices: ["Aserai Archer", "Aserai Footman", "Battanian Fian", "Imperial Archer", "Imperial Legionary", "Khuzait Horse Archer", "Sturgian Spearman", "Vlandian Banner Knight"].map((Label, i) => ({ Label, Detail: `${12 + i * 2} available`, Enabled: true }))
        .concat([{ Label: "Lord's bodyguard", Detail: "This person does not command these troops.", Enabled: false }])
    });
    if (preset === "action-builder-bargain" || preset === "action-builder-npc-offer") Object.assign(result, {
      Title: "Your sentence", CanAccept: true,
      Sentence: "I wish to give you 12 Imperial Legionaries; and I will give you 8 Battanian Fians; and you will give me 15,000 denars; and you will give me 4 war horses.",
      Choices: choices(["and I will give…", "and you will give…", "Add another action…", "Edit a term or its timing…", "Payment: after verified fulfillment"])
    });
    if (preset === "action-builder-npc-offer") {
      result.Sentence = "I would like you to give me the prisoner Lord Neretzes; and I will give you 25,000 denars.";
      result.Choices.unshift({ Label: "Decline this offer", Detail: "", Enabled: true });
    }
    if (preset === "action-builder-marriage-offer") Object.assign(result, {
      Title: "Answer the arranged marriage offer", SearchVisible: true, CanAccept: false,
      Choices: [
        { Label: "Accept this marriage proposal", Detail: "Liena and Aeric will join Clan Seanel. Lucon offers 20,000 denars and two horses.", Enabled: true },
        { Label: "Refuse this marriage proposal", Detail: "Offer expires on campaign day 164.", Enabled: true },
        { Label: "Request a meeting with Liena…", Detail: "Estimates include the courier.", Enabled: true }
      ]
    });
    if (preset === "action-builder-wedding-towns") Object.assign(result, {
      Title: "Request a meeting", Sentence: "Liena and Aeric will join Clan Seanel.", SearchVisible: true, CanAccept: false,
      Choices: [{ Label: "Choose town ▴", Detail: "", Enabled: true }].concat(
        ["Amitatys", "Amprela", "Argoron", "Balgard", "Car Banseth", "Dunglanys", "Epicrotea", "Galend"]
          .map((Label, index) => ({ Label, Detail: index + 1 + " d 3 hr", Enabled: true })))
    });
    if (preset === "action-builder-amount") Object.assign(result, {
      Title: "Amount of gold", EntryVisible: true, Choices: [], $emptyListRationales: { Choices: "The amount step uses the number field and Continue." }
    });
    if (preset === "action-builder-settlements") Object.assign(result, {
      Title: "Select settlement", SearchVisible: true,
      Choices: choices(["Amitatys", "Amprela", "Argoron", "Askar", "Balgard", "Car Banseth", "Chaikand", "Danustica", "Dunglanys", "Epicrotea", "Galend", "Hubyar", "Husn Fulq", "Jaculan", "Jalmarys", "Lageta"])
    });
    if (preset === "action-builder-parties") Object.assign(result, {
      Title: "Select political party", SearchVisible: true,
      Choices: choices(["Crown Loyalists", "Freeholders", "Noble Assembly", "Town League"])
    });
    if (preset === "action-builder-support") Object.assign(result, {
      Title: "What support are you proposing?", SearchVisible: true,
      Choices: choices(["Back a political platform", "Join a political party", "Support a proposal", "Support me when I declare rebellion"])
    });
    if (preset === "action-builder-pledge") Object.assign(result, {
      Title: "Your sentence", CanAccept: true,
      Sentence: "I would like you to support me when I declare rebellion against Lucon, ruler of the Northern Empire.",
      Choices: choices(["and you will give…", "and I will give…", "Add another action…", "Edit a term or its timing…"])
    });
    if (preset === "action-builder-declaration") Object.assign(result, {
      Title: "Your sentence", CanAccept: true,
      Sentence: "I declare rebellion against your rule of the Northern Empire, as leader of the House of Varos.",
      Choices: [{ Label: "Declared when you send this message", Detail: "The ruler cannot veto it. You can still go Back or Cancel.", Enabled: false }]
    });
    if (preset === "action-builder-error") Object.assign(result, {
      Title: "Select troops", SearchVisible: true, Error: "This person no longer commands the selected troops.",
      Choices: [{ Label: "Imperial Legionary", Detail: "No longer available", Enabled: false }]
    });
    result.Recipient = preset === "action-builder-pledge" ? "To Argoros" : "To Lucon";
    result.Choices = result.Choices.map(row => ({ ...row, Disabled: !row.Enabled }));
    return result;
  }
  if (normalizedName === "ReignClanAccordsScreen.xml") return getClanAccordsSampleData(preset);
  if (normalizedName === "ReignGovernmentScreen.xml") return getGovernmentHearingSampleData(preset);
  if (normalizedName === "ReignTavernHouseScreen.xml") return getTavernHouseSampleData(preset);
  const rootKeys = PREFAB_ROOT_KEYS[normalizedName];
  const base = rootKeys
    ? Object.fromEntries(rootKeys.filter((key) => Object.prototype.hasOwnProperty.call(SAMPLE_DATA, key)).map((key) => [key, SAMPLE_DATA[key]]))
    : SAMPLE_DATA;
  const result = {
    ...base,
    ...(PREFAB_SAMPLE_OVERRIDES[normalizedName] || {}),
    ...(PREVIEW_PRESET_OVERRIDES[preset] || {})
  };
  if (normalizedName === "ReignSocialEventScreen.xml" && preset === "wedding-vows") Object.assign(result, {
    EventTitle: "Wedding ceremony",
    PhaseTitle: "Vows and farewell",
    PhaseStatus: "Phase 3 of 3 | Attendees 12",
    PhaseDescription: "The witnesses hear the vows, and the two households exchange their farewells.",
    ApproachLabel: "Wedding actions", NextPhaseLabel: "Conclude ceremony", LeaveEventLabel: "Leave ceremony"
  });
  if (normalizedName === "ReignCorrespondenceScreen.xml" && preset === "marriage-letter") Object.assign(result, {
    SelectedName: "Lucon", SelectedSubtitle: "Clan Osticos",
    BusyText: "View marriage offer", CanOpenMarriageOffer: true,
    ChatLines: [{ Speaker: "Lucon", Text: "I propose Liena and Aeric in marriage. The couple will belong to Clan Seanel. I offer 20,000 denars and two horses. This offer is open for thirty days from delivery.", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false }],
    ChatScrollVersion: 1
  });
  if (normalizedName === "ReignDiplomacyAnnouncementScreen.xml" && preset.startsWith("news-")) {
    Object.assign(result, { Title: "News of the Realm", DateText: "Day 130", ActorName: "Caladog", ActorKingdom: "Battania", ActorReason: "The ruling authority", ActorPortraitId: "caladog_portrait", ActorPortraitCacheKey: "caladog_portrait", ActorPortraitAsset: ASSETS.caladog, TargetName: "Derthert", TargetKingdom: "Vlandia", TargetReason: "The other ruler", TargetPortraitId: "derthert_portrait", TargetPortraitCacheKey: "derthert_portrait", TargetPortraitAsset: ASSETS.derthert, ShowTarget: true });
    const news = {
      "news-capture": ["A Leader Taken Captive", "News of a capture", "Derthert, ruler of Vlandia, has been taken prisoner by Caladog.", "No sentence has yet been announced."],
      "news-sentence": ["A Sentence of Death", "Fifteen days until execution", "Caladog has condemned Derthert to death. The execution is to take place fifteen days after this sentence, without torture or humiliation.", "No ransom will be accepted. Escape or rescue would void this sentence and leave the war unresolved."],
      "news-execution": ["An Execution Announced", "The sentence is carried out", "Derthert has been executed on the orders of Caladog.", ""],
      "news-clan": ["News from the Royal Court", "A Dispute over Pasture Rights", "Caladog of Battania has heard a dispute between Fen Gruffendoc and Fen Uvain. Both houses claim seasonal grazing rights over the same uplands.", "Caladog has ruled in favor of Fen Gruffendoc. Fen Uvain leaves court embittered, and the feud between the houses remains unresolved."],
      "news-foreign": ["News Between the Realms", "Smuggling Crisis", "Caladog of Battania rejected the demand for stronger action against protected smugglers put forward by nobles in Battania. The petition concerned relations with Vlandia.\n\nDerthert of Vlandia rejected the demand for stronger action against protected smugglers put forward by nobles in Vlandia. The petition concerned relations with Battania.\n\nThe petition: Border magnates demand stronger action against protected smugglers. Settlement notables add their voices to the noble petition.", "Both refusals angered the petitioning nobles, damaging their relations with their own ruler.\n\nIn Battania, calls for confrontation or cooperation with Vlandia are unchanged.\n\nIn Vlandia, calls for confrontation or cooperation with Battania are unchanged.\n\nThese rulings did not launch enforcement action or change a treaty. Any further action remains a separate decision."],
      "news-world": ["A Turn in the Realm's Fortune", "Famine", "Famine has struck Battania. Its villages are producing less food, placing the realm's supplies under strain.", "These conditions will last for 30 days."],
      "news-golden-age": ["A Turn in the Realm's Fortune", "Golden Age", "A golden age of building has begun in Battania. Construction is advancing more swiftly throughout its towns and castles.", "These conditions will last for 30 days."]
    }[preset];
    if (news) [result.Title, result.Outcome, result.Summary, result.Terms] = news;
    if (preset === "news-world" || preset === "news-golden-age") Object.assign(result, { ShowTarget: false, TargetName: "", TargetKingdom: "", TargetReason: "", ActorReason: "The affected realm" });
  }
  if (normalizedName === "ReignDiplomacyAnnouncementScreen.xml") {
    Object.assign(result, { IsStandardAnnouncement: !preset.startsWith("government-news-"), IsGovernmentNews: preset.startsWith("government-news-"),
      NewsKingdoms: [], NewsKingdomCount: 0, GovernmentNewsHeading: "", GovernmentNewsBody: "" });
    if (preset.startsWith("government-news-")) {
      const names = ["Southern Empire", "Northern Empire", "Western Empire", "Vlandia", "Battania", "Sturgia", "Aserai", "Khuzait"];
      const articles = names.map((name, i) => `${name} — ${i < 3 ? "Senate" : "Council"}\n\nDECIDED — ${i % 2 ? "Village relief" : "Trial by Jury"}: ${i % 2 ? "Approved. Grain deliveries remain outstanding and will be recorded when received." : "Enacted by the weighted vote. The law is now in force."}\n\nPRESENTED — ${i % 2 ? "Charter of Local Customs" : "Castle-Guard Service"}. The government heard the proposal and its costs. The next decision is scheduled for Spring 30. Members may change their declared positions before that session.`);
      Object.assign(result, { Title: "The Reign Herald", DateText: "Proceedings of Spring 15, 1088", GovernmentNewsHeading: "All kingdoms",
        NewsKingdoms: names.map(Name => ({ Name, Summary: "1 presented · 1 decided" })), NewsKingdomCount: names.length,
        GovernmentNewsBody: articles.join("\n\n") });
      if (preset === "government-news-kingdom") Object.assign(result, { GovernmentNewsHeading: "Southern Empire — Senate", GovernmentNewsBody: articles[0] });
      if (preset === "government-news-long") result.GovernmentNewsBody = articles.map(article => article + "\n\n" + "The petitioners named the affected villages and explained the obligations that remain to be fulfilled. The Crown's representative recorded a public reason, while the detailed terms remain available in the government archive. ".repeat(8)).join("\n\n");
      if (preset === "government-news-empty") Object.assign(result, { NewsKingdoms: [], NewsKingdomCount: 0, GovernmentNewsBody: "No proceedings have been published yet. The next edition follows the next government session." });
    }
    if (preset === "diplomacy-long") Object.assign(result, {
      Outcome: "Envoys return with terms for a lasting settlement along the disputed border",
      Summary: "The envoys have returned from the border with the terms of a proposed settlement. Both courts have heard the accounts of merchants, village elders and the commanders responsible for guarding the roads. The agreement sets out how disputes will be heard and how ordinary traffic can resume without further seizures.\n\nThe disputed crossing will remain open to caravans from either realm. Its watchmen will record the goods that pass through, and no additional toll may be collected without the consent of both courts. Traders who can prove that their goods were taken during the dispute will be allowed to present their claims before the next gathering of the council.\n\nThe frontier villages will receive notice before either side moves troops through their fields. Patrol commanders must respect the harvest and keep their soldiers away from stored grain. Any complaint against a patrol will be delivered to both rulers, together with the names of those who witnessed the incident.",
      Terms: "Prisoners covered by the agreement will be exchanged at the crossing. Each court will name an envoy to witness their return and record any claim that remains unsettled. Families waiting for news will be informed before the escorts depart.\n\nThe settlement will be read aloud in the market towns along the route. Copies will be sent to the noble houses responsible for the border, so that their stewards and garrisons receive the same instructions. Neither court will treat an unverified rumor as grounds for breaking the agreement.",
      ActorReason: "The people of the frontier have paid dearly for this quarrel. Our merchants need open roads and our farmers need to bring in their harvest without fear of another patrol taking the grain. I have accepted these terms so that both courts can answer future grievances before they become another cause for war.",
      TargetReason: "The agreement protects the crossing without giving either side the right to seize it by force. I expect every commander under my banner to honor the settlement, and I will hear the families whose losses have yet to be answered."
    });
    const paragraphs = [result.Summary, result.Terms];
    if (!preset.startsWith("news-")) {
      if (result.ActorReason?.trim()) paragraphs.push(`${result.ActorName} gave this reason: “${result.ActorReason.trim()}”`);
      if (result.TargetReason?.trim()) paragraphs.push(`${result.TargetName} gave this reason: “${result.TargetReason.trim()}”`);
      result.ActorReason = "Announcing ruler";
      result.TargetReason = "Responding ruler";
    }
    result.ReportText = paragraphs.filter(value => value?.trim()).map(value => value.trim()).join("\n\n");
  }
  if (normalizedName === "ReignPlayerApproachPopup.xml" && preset === "approach-keep") Object.assign(result, {
    Detail: "Lucon, Rhagaea, Caladog and Mesui wish to speak with you.", AcceptText: "See what they want", DeclineText: "Avoid them"
  });
  if (normalizedName === "ReignBanterPanel.xml") {
    if (preset === "banter-collapsed") Object.assign(result, { Collapsed: true, Expanded: false, PanelLeft: 80, PanelTop: 60, PanelHeight: 42, CollapseText: "Expand" });
    if (preset === "banter-generating") Object.assign(result, { Transcript: "", Status: "Someone is about to speak…" });
    if (preset === "banter-takeover") Object.assign(result, { CanJoin: false, Status: "Joining…" });
    if (preset === "banter-long") {
      Object.assign(result, { PanelLeft: 120, PanelTop: 80, PanelWidth: 640, PanelHeight: 380, DragWidth: 476, TitleWidth: 440, TranscriptWidth: 610, TranscriptHeight: 304, StatusWidth: 564 });
      result.Transcript = Array.from({length:12}, (_,i) => (i%2 ? "Jon" : "Rhagaea") + ": " + "We will find a dry place before nightfall, if the weather permits. ".repeat(15)).join("\n\n");
    }
  }
  if (normalizedName === "ReignPlayerApproachPopup.xml" && preset === "approach-patrol") Object.assign(result, {
    Title: "A NOBLE'S PARTY", Detail: "Caladog's party is approaching you.", AcceptText: "Stop and see what they want", DeclineText: "Nod and pass"
  });
  if (normalizedName === "ReignPartyChatScreen.xml" && preset.startsWith("homes-")) {
    const atHome = preset === "homes-private-chat";
    const empty = preset === "homes-empty";
    const people = [
      ["Livia Bellori", "Tavern worker", ASSETS.ira], ["Oren Mercato", "Merchant", ASSETS.mengus],
      ["Beatrice Voss", "Artisan", ASSETS.adalindis], ["Caius Orlandi", "Guard", ASSETS.derthert],
      ["Zara Caldera", "Resident", ASSETS.abagai], ["Tobias Marin", "Barber", ASSETS.aeron],
      ["Hilda Rainault", "Tavern keeper", ASSETS.corein], ["Lucian Ferrano", "Smith", ASSETS.caladog]
    ];
    Object.assign(result, {
      IsHomesMode: true, IsHomeDirectory: !atHome, HasChatSession: atHome, AllowGroupSelection: false,
      ShowLifeControls: false, ShowStandardComposer: true, ComposerWidth: 1192, SendButtonLeft: 1388, SendButtonWidth: 202, CanAnnounce: false,
      Title: "Homes — Zeonica", LocationText: atHome ? "At home with Livia Bellori" : "People you have met here",
      RosterTitle: "Known residents", ClearSelectionText: "Homes", InputEnabled: atHome,
      SelectedSummary: atHome ? "Private visit · only you and Livia Bellori can hear" : `${empty ? 0 : people.length} known residents at home`,
      HomeDirectoryText: empty ? "You have not met anyone living here yet. Speak with people in the settlement to get to know them."
        : "Select a resident's card to chat at their home, or choose Visit in person to meet them in the settlement.",
      PartyMembers: empty ? [] : people.map(([Name, DisplayStatus, asset], i) => ({
        ...portraitFields(`resident_${i}`, asset), Name, DisplayStatus, StatusTop: 56, StatusHeight: 21,
        IsActive: atHome && i === 0, IsInactive: !atHome || i !== 0, CanVisitInPerson: true
      })),
      ChatLines: atHome ? [
        { Speaker: "Livia Bellori", Text: '"Come in. The kettle is warm, and there is a seat by the window."', IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false },
        { Speaker: "You", Text: "How have things been in Zeonica?", IsNpcLine: false, IsPlayerLine: true, IsSystemLine: false }
      ] : [],
      $emptyListRationales: { PartyMembers: "No residents have been met in this settlement yet.", ChatLines: "The Homes directory starts no conversation; selecting one resident opens their private session." }
    });
  }
  if (preset === "intoxication-actions") result.ChatLines = [
    { Speaker: "You", Text: "Perhaps you should rest.", Role: "user", IsPlayerLine: true, IsNpcLine: false, IsSystemLine: false },
    ...[
      '*They take a sip of wine, smiling faintly.* "A pleasant evening."',
      '"I am perfectly steady." *They reach for the table and miss, then laugh at themselves.*',
      '*Their eyes drift shut. Too sick to sit upright, they stir and mumble.* "No more..."'
    ].map((Text) => ({ Speaker: "Gwydan", Text, Role: "assistant", IsNpcLine: true, IsPlayerLine: false, IsSystemLine: false }))
  ];
  if (["ReignIndividualChatScreen.xml", "ReignPartyChatScreen.xml", "ReignCastleChatScreen.xml"].includes(fileName)) {
    result.ChatScrollVersion = 1;
    result.View = { CanView: true, ButtonText: "View", IsEnlarged: false, EnlargedImageId: "", EventImageAsset: "" };
    if (preset.startsWith("view-")) {
      const state = preset.slice(5), ready = ["ready", "enlarged", "long"].includes(state);
      const asset = "assets/chat-view-fixture.svg";
      result.View = { CanView: state !== "pending", ButtonText: state === "pending" ? "Viewing…" : "View", IsEnlarged: state === "enlarged", EnlargedImageId: ready ? "view-fixture" : "", EventImageAsset: asset };
      result.IsBusy = false; result.InputEnabled = true; result.BusyText = "";
      // Put the image card in the visible viewport for deterministic geometry proof.
      result.ChatLines = ready ? [] : (result.ChatLines || []).slice(-1);
      if (state === "long") result.ChatLines = Array.from({length: 20}, (_, index) => ({ Speaker: index % 2 ? "Ira" : "You", Text: "The party exchanges news as dusk falls across the camp. ".repeat(8), IsPlayerLine: index % 2 === 0, IsNpcLine: index % 2 === 1 }));
      result.ChatLines = [...(result.ChatLines || []).slice(-8), { Speaker: "View", Text: state === "failed" ? "A full-body reference is missing. Use the portrait control first." : ready ? "A letter changes hands at dusk." : "Preparing a view of this scene…", IsViewLine: true, HasSceneImage: ready, SceneImageId: ready ? "view-fixture" : "", EventImageAsset: asset, SceneCaptionTop: ready ? 220 : 12, SceneCompactCaptionTop: ready ? 112 : 12 }];
    }
  }
  if (normalizedName === "ReignPartyChatScreen.xml") {
    result.ShowPartyContinuation = !result.IsHomesMode;
    result.CanContinuePartyChat = false;
    if (preset.startsWith("continue-")) {
      const available = preset === "continue-available", restored = preset === "continue-restored";
      Object.assign(result, { ShowPartyContinuation: true, CanContinuePartyChat: available, InputText: "", SendLabel: "Approach", IsBusy: false,
        SelectedSummary: restored ? "Selected: Ira, Rhagaea" : "No characters selected",
        ChatLines: restored ? [
          { Speaker: "You", Text: "We agreed to meet at dawn. Shall we continue with the eastern road?", IsPlayerLine: true, IsNpcLine: false },
          { Speaker: "Rhagaea", Text: "The eastern road, then. We were deciding who should ride ahead.", IsPlayerLine: false, IsNpcLine: true }
        ] : [{ Speaker: "Party", Text: "Select characters above, then speak to them as a group.", IsSystemLine: true }]
      });
    }
    result.PartyContinuationUnavailable = !result.CanContinuePartyChat;
  }
  if (result.View) result.View.IsUnavailable = !result.View.CanView;
  if (Array.isArray(result.ChatLines)) result.ChatLines = result.ChatLines.map((line) => ({
    HasSceneImage: false, SceneImageId: "", SceneCaptionTop: 12, SceneCompactCaptionTop: 12, SceneCaption: line.Text,
    ...line, IsViewLine: !!line.IsViewLine, IsTextLine: !line.IsViewLine, IsNonPlayerLine: !line.IsPlayerLine && !line.IsViewLine,
    RichText: previewActionRichText(line.Text, line.IsNpcLine || line.Role === "npc" || line.Role === "assistant")
  }));
  if (normalizedName === "ReignWarCouncilScreen.xml") {
    result.PeaceForceLeftOptions = result.PeaceForceOptions.slice(0, 6);
    result.PeaceForceRightOptions = result.PeaceForceOptions.slice(6);
  }
  return result;
}

export function getPreviewFixtureInventory() {
  return Object.keys(PREFAB_ROOT_KEYS).sort().map((fileName) => ({
    fileName,
    rootKeys: [...PREFAB_ROOT_KEYS[fileName]],
    hasOverride: Object.prototype.hasOwnProperty.call(PREFAB_SAMPLE_OVERRIDES, fileName)
  }));
}

export function applyCourtState(state) {
  SAMPLE_DATA.HomeVisible = state === "home";
  SAMPLE_DATA.LargeHomeVisible = SAMPLE_DATA.HomeVisible && SAMPLE_DATA.UseLargeCourtLayout;
  SAMPLE_DATA.ReferenceHomeVisible = SAMPLE_DATA.HomeVisible && !SAMPLE_DATA.UseLargeCourtLayout;
  SAMPLE_DATA.TabContentVisible = state !== "home";
  SAMPLE_DATA.DetailVisible = state === "detail" || state === "audience";
  SAMPLE_DATA.AudienceVisible = state === "audience";
  SAMPLE_DATA.ActionsVisible = state === "detail";
}
