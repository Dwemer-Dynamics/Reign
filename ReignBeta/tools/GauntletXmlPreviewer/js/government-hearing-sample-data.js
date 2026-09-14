// Supplied by the Government feature owner; see retained preview-fixture.json.
const fixture = {
  "schema": "reign-government-preview-fixture-v2",
  "prefab": "ReignGovernmentScreen.xml",
  "rootKeys": [
    "Title",
    "KingdomName",
    "AuthorityText",
    "ModeText",
    "BusinessTabText",
    "BusinessTabSprite",
    "HearingTabSprite",
    "MembersTabSprite",
    "ArchiveTabSprite",
    "MeetingText",
    "IsHearingView",
    "IsMembersView",
    "IsArchiveView",
    "IsAuthorityView",
    "LeftHeading",
    "CenterHeading",
    "RightHeading",
    "StageText",
    "MatterKind",
    "MatterTitle",
    "MatterSummary",
    "OutcomeText",
    "RecommendationText",
    "AuthorityExplanation",
    "RecessText",
    "AttendanceText",
    "StatusText",
    "ComposerText",
    "IsInteractive",
    "IsReadOnly",
    "HasMatter",
    "HasOutcome",
    "HasParticipants",
    "HasBusiness",
    "HasArchive",
    "CanRecommend",
    "CanCallVote",
    "CanPostpone",
    "CanReconvene",
    "CanAdoptPetition",
    "CanConfirmRecommendation",
    "CanOverrideRecommendation",
    "CanAcceptGovernmentDecision",
    "AcceptGovernmentText",
    "CanSpeak",
    "ShowComposer",
    "BusinessCount",
    "ArchiveCount",
    "MemberCount",
    "ParticipantCount",
    "TranscriptCount",
    "OptionCount",
    "PartyCount",
    "ObligationCount",
    "BusinessItems",
    "ArchiveItems",
    "Options",
    "Participants",
    "Members",
    "Transcript",
    "Parties",
    "Obligations",
    "CommitmentCount",
    "Commitments",
    "KingdomBannerId",
    "KingdomBannerArgs",
    "KingdomBannerProvider",
    "GovernmentSceneId",
    "IsPolicyView",
    "PolicyChoiceCount",
    "PolicyChoices",
    "OptionRows",
    "OptionRowCount",
    "Petitioner",
    "Sponsor",
    "HasPetitioner",
    "HasSponsor"
  ],
  "base": {
    "Title": "IMPERIAL SENATE",
    "KingdomName": "Southern Empire",
    "AuthorityText": "LEVEL 5 · THE ASSEMBLY DECIDES",
    "ModeText": "PUBLIC GOVERNMENT RECORD",
    "BusinessTabText": "Business  4",
    "BusinessTabSprite": "reign_government_tab_business",
    "HearingTabSprite": "reign_government_tab_hearing_selected",
    "MembersTabSprite": "reign_government_tab_members",
    "ArchiveTabSprite": "reign_government_tab_archive",
    "MeetingText": "Next seasonal meeting: day 211.0",
    "IsHearingView": true,
    "IsMembersView": false,
    "IsArchiveView": false,
    "IsAuthorityView": false,
    "LeftHeading": "PENDING BUSINESS",
    "CenterHeading": "PUBLIC HEARING",
    "RightHeading": "PEOPLE & ATTENDANCE",
    "StageText": "In discussion",
    "MatterKind": "POLICY PETITION",
    "MatterTitle": "Trial by Jury",
    "MatterSummary": "Lord Phaedon requests adoption of Trial by Jury. The Senate must decide whether to enact the policy.",
    "OutcomeText": "",
    "RecommendationText": "Ruler recommendation: Enact the policy",
    "AuthorityExplanation": "Members weigh your recommendation, their interests, and prior commitments. The assembly's vote is binding.",
    "RecessText": "Postpone this hearing once to meet attending members at the capital. Absent members still vote.",
    "AttendanceText": "22 seated members · 4 absent",
    "StatusText": "Hear the case, recommend an outcome, and record the decision.",
    "ComposerText": "",
    "IsInteractive": true,
    "IsReadOnly": false,
    "HasMatter": true,
    "HasOutcome": false,
    "HasParticipants": true,
    "HasBusiness": true,
    "HasArchive": true,
    "CanRecommend": true,
    "CanCallVote": true,
    "CanPostpone": true,
    "CanReconvene": false,
    "CanAdoptPetition": false,
    "CanConfirmRecommendation": false,
    "CanOverrideRecommendation": false,
    "CanAcceptGovernmentDecision": false,
    "AcceptGovernmentText": "Accept: Enact the policy",
    "CanSpeak": false,
    "ShowComposer": true,
    "BusinessCount": 4,
    "ArchiveCount": 2,
    "MemberCount": 4,
    "ParticipantCount": 2,
    "TranscriptCount": 3,
    "OptionCount": 2,
    "PartyCount": 3,
    "ObligationCount": 1,
    "BusinessItems": [
      {
        "Title": "Trial by Jury",
        "Summary": "Policy petition · In discussion",
        "IsSelected": true,
        "CardSprite": "reign_government_business_policy_selected"
      },
      {
        "Title": "Allocate Hertogea Castle",
        "Summary": "Fief allocation · Urgent",
        "IsSelected": false,
        "CardSprite": "reign_government_business_fief"
      },
      {
        "Title": "Peace with Vlandia",
        "Summary": "Diplomacy · Awaiting hearing",
        "IsSelected": false,
        "CardSprite": "reign_government_business_diplomacy"
      },
      {
        "Title": "Relief for raided villages",
        "Summary": "Seasonal demand · In progress",
        "IsSelected": false,
        "CardSprite": "reign_government_business_seasonal"
      }
    ],
    "ArchiveItems": [
      {
        "Title": "Granary supplies",
        "Summary": "Seasonal demand · Decided",
        "IsSelected": false,
        "CardSprite": "reign_government_business_seasonal"
      },
      {
        "Title": "Trade with Battania",
        "Summary": "Trade agreement · Decided",
        "IsSelected": false,
        "CardSprite": "reign_government_business_diplomacy"
      }
    ],
    "Options": [
      {
        "Label": "Enact the policy",
        "Detail": "Adopt the proposed policy.",
        "IsSelected": true,
        "IsEnabled": true,
        "SelectionText": "Recommended",
        "CardSprite": "reign_government_option_selected",
        "RecommendationSprite": "reign_government_recommendation_selected"
      },
      {
        "Label": "Keep current law",
        "Detail": "Leave the existing policy unchanged.",
        "IsSelected": false,
        "IsEnabled": true,
        "SelectionText": "",
        "CardSprite": "reign_government_option_card",
        "RecommendationSprite": "reign_government_recommendation"
      }
    ],
    "Participants": [
      {
        "Name": "Lord Phaedon",
        "Role": "Petitioner",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Mengus (lord_5_7)/portrait_chest.png",
        "PortraitId": "lord_5_7",
        "PortraitCacheKey": "preview_Lord Phaedon",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      {
        "Name": "Senator Valeria",
        "Role": "Sponsor",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Ira (lord_1_37)/portrait_chest.png",
        "PortraitId": "lord_1_37",
        "PortraitCacheKey": "preview_Senator Valeria",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      }
    ],
    "Members": [
      {
        "Name": "Lord Phaedon",
        "Role": "Petitioner",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Mengus (lord_5_7)/portrait_chest.png",
        "PortraitId": "lord_5_7",
        "PortraitCacheKey": "preview_Lord Phaedon",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      {
        "Name": "Senator Valeria",
        "Role": "Sponsor",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Ira (lord_1_37)/portrait_chest.png",
        "PortraitId": "lord_1_37",
        "PortraitCacheKey": "preview_Senator Valeria",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      {
        "Name": "Senator Marcellus",
        "Role": "Common Weal Coalition",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Caladog (lord_5_1)/portrait_chest.png",
        "PortraitId": "lord_5_1",
        "PortraitCacheKey": "preview_Senator Marcellus",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      {
        "Name": "Senator Eutropios",
        "Role": "Free Estates",
        "Constituency": "Amitatys",
        "Attendance": "Absent · Serving with an army",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_shared/Derthert (lord_4_1)/source.png",
        "PortraitId": "lord_4_1",
        "PortraitCacheKey": "preview_Senator Eutropios",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      }
    ],
    "Transcript": [
      {
        "Name": "Senator Valeria",
        "Role": "Sponsor",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Ira (lord_1_37)/portrait_chest.png",
        "PortraitId": "lord_1_37",
        "PortraitCacheKey": "preview_Senator Valeria",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "Speaker": "Senator Valeria · Sponsor",
        "Text": "The people I represent ask for a fair hearing before punishment.",
        "Date": "Day 208.0",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      {
        "Name": "Lord Phaedon",
        "Role": "Petitioner",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Mengus (lord_5_7)/portrait_chest.png",
        "PortraitId": "lord_5_7",
        "PortraitCacheKey": "preview_Lord Phaedon",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "Speaker": "Lord Phaedon · Petitioner",
        "Text": "I ask the Senate to support this petition.",
        "Date": "Day 208.0",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      {
        "Name": "Senator Marcellus",
        "Role": "Common Weal Coalition",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Caladog (lord_5_1)/portrait_chest.png",
        "PortraitId": "lord_5_1",
        "PortraitCacheKey": "preview_Senator Marcellus",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "Speaker": "Senator Marcellus · Opposing speaker",
        "Text": "How will the new law protect local order?",
        "Date": "Day 208.0",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      }
    ],
    "Parties": [
      {
        "Name": "Common Weal Coalition",
        "Planks": "Popular welfare · Trade · Infrastructure",
        "Speaker": "Senator Valeria · 9 seats",
        "Statement": "Relief must reach the western villages before winter."
      },
      {
        "Name": "Crown and Legion",
        "Planks": "Royal authority · Military strength",
        "Speaker": "Senator Marcellus · 6 seats",
        "Statement": "The frontier must not be stripped."
      },
      {
        "Name": "Free Estates",
        "Planks": "Local autonomy · Trade",
        "Speaker": "Senator Eutropios · 4 seats",
        "Statement": "Any levy must be bounded and recorded."
      }
    ],
    "Obligations": [
      {
        "Title": "Relief for raided villages",
        "Detail": "114 / 160 · Due day 218.0",
        "CanContribute": true
      }
    ],
    "CommitmentCount": 1,
    "Commitments": [
      {
        "Name": "Senator Valeria",
        "Matter": "Trial by Jury",
        "Terms": "Support pledged after relief reaches Amitatys.",
        "Status": "Active · Accepted day 208.0 · Expires day 215.0"
      }
    ],
    "KingdomBannerId": "preview_empire",
    "KingdomBannerArgs": "",
    "KingdomBannerProvider": "BannerImageTextureProvider",
    "KingdomBannerAsset": "assets/court-player-banner.svg",
    "GovernmentSceneId": "reigneventart|court_petition_reference|empire",
    "EventImageAsset": "../../GUI/UiCalibration/reference-scenes/ruler-petition-throne-viewpoint-empire.png",
    "IsPolicyView": false,
    "PolicyChoiceCount": 3,
    "PolicyChoices": [
      {
        "Title": "Enact Trial by Jury",
        "Detail": "Introduce the policy for public hearing and a constitutional vote.",
        "IsEnabled": true
      },
      {
        "Title": "Review Hertogea Castle",
        "Detail": "Ask the government to reconsider the assignment of this fief.",
        "IsEnabled": true
      },
      {
        "Title": "Review clan membership",
        "Detail": "Present an expulsion proposal to the government.",
        "IsEnabled": true
      }
    ],
    "OptionRows": [
      {
        "Left": {
          "Label": "Enact the policy",
          "Detail": "Adopt the proposed policy.",
          "IsSelected": true,
          "IsEnabled": true,
          "SelectionText": "Recommended",
          "CardSprite": "reign_government_option_selected",
          "RecommendationSprite": "reign_government_recommendation_selected"
        },
        "Right": {
          "Label": "Keep current law",
          "Detail": "Leave the existing policy unchanged.",
          "IsSelected": false,
          "IsEnabled": true,
          "SelectionText": "",
          "CardSprite": "reign_government_option_card",
          "RecommendationSprite": "reign_government_recommendation"
        },
        "HasRight": true
      }
    ],
    "OptionRowCount": 1,
    "HasPetitioner": true,
    "HasSponsor": true,
    "Petitioner": {
      "Name": "Lord Phaedon",
      "Role": "Petitioner",
      "Constituency": "Amitatys",
      "Attendance": "Present · Capital",
      "HasPortrait": true,
      "PortraitAsset": "../../PortraitCache/_preview-government-production/Mengus (lord_5_7)/portrait_chest.png",
      "PortraitId": "lord_5_7",
      "PortraitCacheKey": "preview_Lord Phaedon",
      "PortraitAdditionalArgs": "",
      "PortraitTextureProviderName": "ReignPortraitProvider",
      "HasBanner": true,
      "BannerId": "preview_empire",
      "BannerArgs": "",
      "BannerProvider": "BannerImageTextureProvider",
      "BannerAsset": "assets/court-player-banner.svg"
    },
    "Sponsor": {
      "Name": "Senator Valeria",
      "Role": "Sponsor",
      "Constituency": "Amitatys",
      "Attendance": "Present · Capital",
      "HasPortrait": true,
      "PortraitAsset": "../../PortraitCache/_preview-government-production/Ira (lord_1_37)/portrait_chest.png",
      "PortraitId": "lord_1_37",
      "PortraitCacheKey": "preview_Senator Valeria",
      "PortraitAdditionalArgs": "",
      "PortraitTextureProviderName": "ReignPortraitProvider",
      "HasBanner": true,
      "BannerId": "preview_empire",
      "BannerArgs": "",
      "BannerProvider": "BannerImageTextureProvider",
      "BannerAsset": "assets/court-player-banner.svg"
    }
  },
  "variants": {
    "government-urgent": {
      "CanPostpone": false,
      "MatterKind": "FIEF ALLOCATION",
      "MatterTitle": "Allocate Hertogea Castle",
      "MatterSummary": "The captured castle needs an owner. The assembly will choose between the eligible claimants.",
      "RecessText": "Urgent business must be decided without a political recess.",
      "Options": [
        {
          "Label": "Clan Argoros",
          "Detail": "Eligible claimant",
          "IsSelected": true,
          "IsEnabled": true,
          "SelectionText": "Recommended",
          "CardSprite": "reign_government_option_selected",
          "RecommendationSprite": "reign_government_recommendation_selected"
        },
        {
          "Label": "Clan Comnos",
          "Detail": "Eligible claimant",
          "IsSelected": false,
          "IsEnabled": true,
          "SelectionText": "",
          "CardSprite": "reign_government_option_card",
          "RecommendationSprite": "reign_government_recommendation"
        },
        {
          "Label": "Clan Pethros",
          "Detail": "Eligible claimant",
          "IsSelected": false,
          "IsEnabled": true,
          "SelectionText": "",
          "CardSprite": "reign_government_option_card",
          "RecommendationSprite": "reign_government_recommendation"
        }
      ],
      "OptionCount": 3,
      "OptionRows": [
        {
          "Left": {
            "Label": "Clan Argoros",
            "Detail": "Eligible claimant",
            "IsSelected": true,
            "IsEnabled": true,
            "SelectionText": "Recommended",
            "CardSprite": "reign_government_option_selected",
            "RecommendationSprite": "reign_government_recommendation_selected"
          },
          "Right": {
            "Label": "Clan Comnos",
            "Detail": "Eligible claimant",
            "IsSelected": false,
            "IsEnabled": true,
            "SelectionText": "",
            "CardSprite": "reign_government_option_card",
            "RecommendationSprite": "reign_government_recommendation"
          },
          "HasRight": true
        },
        {
          "Left": {
            "Label": "Clan Pethros",
            "Detail": "Eligible claimant",
            "IsSelected": false,
            "IsEnabled": true,
            "SelectionText": "",
            "CardSprite": "reign_government_option_card",
            "RecommendationSprite": "reign_government_recommendation"
          },
          "Right": {
            "Label": "Clan Pethros",
            "Detail": "Eligible claimant",
            "IsSelected": false,
            "IsEnabled": true,
            "SelectionText": "",
            "CardSprite": "reign_government_option_card",
            "RecommendationSprite": "reign_government_recommendation"
          },
          "HasRight": false
        }
      ],
      "OptionRowCount": 2
    },
    "government-postponed": {
      "CanRecommend": false,
      "CanCallVote": false,
      "CanPostpone": false,
      "CanReconvene": true,
      "ShowComposer": false,
      "StageText": "Seven-day recess",
      "RecessText": "Reconvenes on day 215.0. Attending members can be met privately at the capital.",
      "Options": [
        {
          "Label": "Enact the policy",
          "Detail": "Adopt the proposed policy.",
          "IsSelected": true,
          "IsEnabled": false,
          "SelectionText": "Recommended",
          "CardSprite": "reign_government_option_selected",
          "RecommendationSprite": "reign_government_recommendation_selected"
        },
        {
          "Label": "Keep current law",
          "Detail": "Leave the existing policy unchanged.",
          "IsSelected": false,
          "IsEnabled": false,
          "SelectionText": "",
          "CardSprite": "reign_government_option_card",
          "RecommendationSprite": "reign_government_recommendation"
        }
      ],
      "OptionRows": [
        {
          "Left": {
            "Label": "Enact the policy",
            "Detail": "Adopt the proposed policy.",
            "IsSelected": true,
            "IsEnabled": false,
            "SelectionText": "Recommended",
            "CardSprite": "reign_government_option_selected",
            "RecommendationSprite": "reign_government_recommendation_selected"
          },
          "Right": {
            "Label": "Keep current law",
            "Detail": "Leave the existing policy unchanged.",
            "IsSelected": false,
            "IsEnabled": false,
            "SelectionText": "",
            "CardSprite": "reign_government_option_card",
            "RecommendationSprite": "reign_government_recommendation"
          },
          "HasRight": true
        }
      ],
      "OptionRowCount": 1
    },
    "government-members": {
      "IsHearingView": false,
      "IsMembersView": true,
      "LeftHeading": "POLITICAL PARTIES",
      "CenterHeading": "MEMBERS & PARTIES",
      "RightHeading": "REPRESENTATION",
      "HearingTabSprite": "reign_government_tab_hearing",
      "MembersTabSprite": "reign_government_tab_members_selected"
    },
    "government-archive": {
      "IsHearingView": false,
      "IsArchiveView": true,
      "LeftHeading": "DECISIONS & OBLIGATIONS",
      "CenterHeading": "RECORDED OUTCOME",
      "OutcomeText": "The assembly adopted Trial by Jury. The policy is now active. The ruler's recommendation and all individual ballots are recorded.",
      "HasOutcome": true,
      "HearingTabSprite": "reign_government_tab_hearing",
      "ArchiveTabSprite": "reign_government_tab_archive_selected"
    },
    "government-authority": {
      "IsHearingView": false,
      "IsAuthorityView": true,
      "LeftHeading": "CONSTITUTION",
      "CenterHeading": "AUTHORITY"
    },
    "government-readonly": {
      "IsInteractive": false,
      "IsReadOnly": true,
      "CanRecommend": false,
      "CanCallVote": false,
      "CanPostpone": false,
      "CanSpeak": false,
      "ShowComposer": false,
      "Options": [
        {
          "Label": "Enact the policy",
          "Detail": "Adopt the proposed policy.",
          "IsSelected": true,
          "IsEnabled": false,
          "SelectionText": "Recommended",
          "CardSprite": "reign_government_option_selected",
          "RecommendationSprite": "reign_government_recommendation_selected"
        },
        {
          "Label": "Keep current law",
          "Detail": "Leave the existing policy unchanged.",
          "IsSelected": false,
          "IsEnabled": false,
          "SelectionText": "",
          "CardSprite": "reign_government_option_card",
          "RecommendationSprite": "reign_government_recommendation"
        }
      ],
      "ModeText": "READ-ONLY PUBLIC RECORD",
      "OptionRows": [
        {
          "Left": {
            "Label": "Enact the policy",
            "Detail": "Adopt the proposed policy.",
            "IsSelected": true,
            "IsEnabled": false,
            "SelectionText": "Recommended",
            "CardSprite": "reign_government_option_selected",
            "RecommendationSprite": "reign_government_recommendation_selected"
          },
          "Right": {
            "Label": "Keep current law",
            "Detail": "Leave the existing policy unchanged.",
            "IsSelected": false,
            "IsEnabled": false,
            "SelectionText": "",
            "CardSprite": "reign_government_option_card",
            "RecommendationSprite": "reign_government_recommendation"
          },
          "HasRight": true
        }
      ],
      "OptionRowCount": 1
    },
    "government-empty": {
      "BusinessItems": [],
      "Participants": [],
      "Members": [],
      "Transcript": [],
      "Options": [],
      "BusinessCount": 0,
      "ParticipantCount": 0,
      "MemberCount": 0,
      "TranscriptCount": 0,
      "OptionCount": 0,
      "HasMatter": false,
      "HasParticipants": false,
      "HasBusiness": false,
      "CanRecommend": false,
      "CanCallVote": false,
      "CanPostpone": false,
      "ShowComposer": false,
      "MatterTitle": "The floor is open",
      "MatterKind": "GOVERNMENT BUSINESS",
      "MatterSummary": "There are no pending cases. Event business enters this record as it occurs.",
      "AttendanceText": "No pending assembly attendance",
      "RecommendationText": "",
      "StageText": "No matter selected",
      "OptionRows": [],
      "OptionRowCount": 0,
      "HasPetitioner": false,
      "HasSponsor": false,
      "Petitioner": {
        "Name": "Lord Phaedon",
        "Role": "Petitioner",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Mengus (lord_5_7)/portrait_chest.png",
        "PortraitId": "lord_5_7",
        "PortraitCacheKey": "preview_Lord Phaedon",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      },
      "Sponsor": {
        "Name": "Senator Valeria",
        "Role": "Sponsor",
        "Constituency": "Amitatys",
        "Attendance": "Present · Capital",
        "HasPortrait": true,
        "PortraitAsset": "../../PortraitCache/_preview-government-production/Ira (lord_1_37)/portrait_chest.png",
        "PortraitId": "lord_1_37",
        "PortraitCacheKey": "preview_Senator Valeria",
        "PortraitAdditionalArgs": "",
        "PortraitTextureProviderName": "ReignPortraitProvider",
        "HasBanner": true,
        "BannerId": "preview_empire",
        "BannerArgs": "",
        "BannerProvider": "BannerImageTextureProvider",
        "BannerAsset": "assets/court-player-banner.svg"
      }
    },
    "government-level3": {
      "AuthorityText": "LEVEL 3 · RULER AND GOVERNMENT",
      "StageText": "Reconsider the government's objection",
      "CanPostpone": false,
      "CanCallVote": false,
      "CanAcceptGovernmentDecision": true,
      "CanConfirmRecommendation": true,
      "CanOverrideRecommendation": true,
      "RecessText": "The one permitted recess has been used. Absent members still vote."
    },
    "government-proposals": {
      "IsHearingView": false,
      "IsPolicyView": true,
      "CenterHeading": "INTRODUCE A MATTER"
    }
  }
};
export const GOVERNMENT_ROOT_KEYS = fixture.rootKeys;
export function getGovernmentHearingSampleData(preset = "") { return JSON.parse(JSON.stringify({ ...fixture.base, ...(fixture.variants[preset] || {}) })); }
