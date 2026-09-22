using System;
using System.Collections.Generic;
using System.Linq;
using AIPortraits;
using ReignBeta.CastleChat;
using ReignBeta.Court;
using ReignBeta.UI.EventArt;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCastleLayoutScreenVM : ViewModel
    {
        private readonly Action _close;
        private readonly Action _leaveForChat;
        private readonly Action _openFamily;
        private readonly Action _openGovernment;
        private readonly ReignCourtCampaignBehavior _court;
        private string _statusText;

        public ReignCastleLayoutScreenVM(Action close, Action leaveForChat, Action openFamily,
            Action openGovernment, ReignCourtCampaignBehavior court, bool readOnlyGovernment)
        {
            _close = close; _leaveForChat = leaveForChat; _openFamily = openFamily;
            _openGovernment = openGovernment; _court = court;
            GovernmentButtonText = readOnlyGovernment ? "GOVERNMENT — READ-ONLY" : "GOVERNMENT — RULER ACCESS";
            PlayerPortraitCacheKey = CharacterCacheId.ForHero(Hero.MainHero) ?? string.Empty;
            IReadOnlyList<CastleRoomSessionRecord> schedule = court?.EnsureCastleSchedule() ?? Array.Empty<CastleRoomSessionRecord>();
            CastleMapImageId = ReignEventArtTextureFactory.BuildCastleMapImageId(
                schedule.FirstOrDefault()?.CultureId ?? "generic");
            CastleGardenSlots = Slots(schedule, CastleRoom.CastleGardens, 4);
            NobleSolarSlots = Slots(schedule, CastleRoom.NobleSolar, 4);
            LibrarySlots = Slots(schedule, CastleRoom.Library, 4);
            TrainingYardSlots = Slots(schedule, CastleRoom.TrainingYard, 4);
            InnerCourtyardSlots = Slots(schedule, CastleRoom.InnerCourtyard, 4);
            DiningChamberSlots = Slots(schedule, CastleRoom.DiningChamber, 4);
            StableCourtyardSlots = Slots(schedule, CastleRoom.StableCourtyard, 4);
            PortraitGallerySlots = Slots(schedule, CastleRoom.PortraitGallery, 4);
            MainHallSlots = Slots(schedule, CastleRoom.MainHall, 4);
            RoyalBedroomSlots = Slots(schedule, CastleRoom.RoyalBedroom, 4);
            ChapelSlots = Slots(schedule, CastleRoom.Chapel, 4);
            ThroneRoomSlots = Slots(schedule, CastleRoom.ThroneRoom, 4);
            BathsSlots = Slots(schedule, CastleRoom.Baths, 4);
            BattlementSlots = Slots(schedule, CastleRoom.Battlements, 4);
            GuestBedroomSlots = Slots(schedule, CastleRoom.GuestBedrooms, 20);
            GuestBedroomRow1 = Slice(GuestBedroomSlots, 0, 5);
            GuestBedroomRow2 = Slice(GuestBedroomSlots, 5, 5);
            GuestBedroomRow3 = Slice(GuestBedroomSlots, 10, 5);
            GuestBedroomRow4 = Slice(GuestBedroomSlots, 15, 5);
            _statusText = schedule.Count == 0 ? "The Keep is quiet." : TimeBlockName(schedule[0].TimeBlock) + " in the Keep";
        }

        public MBBindingList<ReignCastleSlotVM> CastleGardenSlots { get; }
        public MBBindingList<ReignCastleSlotVM> NobleSolarSlots { get; }
        public MBBindingList<ReignCastleSlotVM> LibrarySlots { get; }
        public MBBindingList<ReignCastleSlotVM> TrainingYardSlots { get; }
        public MBBindingList<ReignCastleSlotVM> InnerCourtyardSlots { get; }
        public MBBindingList<ReignCastleSlotVM> DiningChamberSlots { get; }
        public MBBindingList<ReignCastleSlotVM> StableCourtyardSlots { get; }
        public MBBindingList<ReignCastleSlotVM> PortraitGallerySlots { get; }
        public MBBindingList<ReignCastleSlotVM> MainHallSlots { get; }
        public MBBindingList<ReignCastleSlotVM> RoyalBedroomSlots { get; }
        public MBBindingList<ReignCastleSlotVM> ChapelSlots { get; }
        public MBBindingList<ReignCastleSlotVM> ThroneRoomSlots { get; }
        public MBBindingList<ReignCastleSlotVM> BathsSlots { get; }
        public MBBindingList<ReignCastleSlotVM> BattlementSlots { get; }
        public MBBindingList<ReignCastleSlotVM> GuestBedroomSlots { get; }
        public MBBindingList<ReignCastleSlotVM> GuestBedroomRow1 { get; }
        public MBBindingList<ReignCastleSlotVM> GuestBedroomRow2 { get; }
        public MBBindingList<ReignCastleSlotVM> GuestBedroomRow3 { get; }
        public MBBindingList<ReignCastleSlotVM> GuestBedroomRow4 { get; }

        [DataSourceProperty] public string PlayerPortraitCacheKey { get; }
        [DataSourceProperty] public string CastleMapImageId { get; }
        [DataSourceProperty] public string GovernmentButtonText { get; }
        [DataSourceProperty] public string StatusText { get => _statusText; private set { if (_statusText == value) return; _statusText = value; OnPropertyChangedWithValue(value); } }

        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteOpenFamilyChambers() => _openFamily?.Invoke();
        public void ExecuteOpenGovernment() => _openGovernment?.Invoke();
        public void ExecuteSelectCastleGardens() => Enter(CastleRoom.CastleGardens);
        public void ExecuteSelectNobleSolar() => Enter(CastleRoom.NobleSolar);
        public void ExecuteSelectLibrary() => Enter(CastleRoom.Library);
        public void ExecuteSelectTrainingYard() => Enter(CastleRoom.TrainingYard);
        public void ExecuteSelectInnerCourtyard() => Enter(CastleRoom.InnerCourtyard);
        public void ExecuteSelectDiningChamber() => Enter(CastleRoom.DiningChamber);
        public void ExecuteSelectStableCourtyard() => Enter(CastleRoom.StableCourtyard);
        public void ExecuteSelectPortraitGallery() => Enter(CastleRoom.PortraitGallery);
        public void ExecuteSelectMainHall() => Enter(CastleRoom.MainHall);
        public void ExecuteSelectRoyalBedroom() => Enter(CastleRoom.RoyalBedroom);
        public void ExecuteSelectChapel() => Enter(CastleRoom.Chapel);
        public void ExecuteSelectThroneRoom() => Enter(CastleRoom.ThroneRoom);
        public void ExecuteSelectBaths() => Enter(CastleRoom.Baths);
        public void ExecuteSelectBattlements() => Enter(CastleRoom.Battlements);
        public void ExecuteSelectGuestBedrooms() => Enter(CastleRoom.GuestBedrooms);

        private void Enter(CastleRoom room, string guest = "")
        {
            CastleRoomSessionRecord session = _court?.GetCastleRoomSession(room, guest);
            if (session == null) { StatusText = "That room is unavailable."; return; }
            StatusText = "Heading to " + RoomName(room) + "…";
            ReignCastleChatPreparation.Begin(session, _leaveForChat);
        }

        private MBBindingList<ReignCastleSlotVM> Slots(IReadOnlyList<CastleRoomSessionRecord> sessions, CastleRoom room, int capacity)
        {
            List<string> ids = ReignCourtCampaignBehavior.SplitIds(sessions.FirstOrDefault(x => x.Room == (int)room)?.OccupantHeroIdsCsv);
            var list = new MBBindingList<ReignCastleSlotVM>();
            for (int i = 0; i < capacity; i++)
            {
                string id = i < ids.Count ? ids[i] : string.Empty;
                Hero hero = string.IsNullOrWhiteSpace(id) ? null : Hero.FindFirst(x => x.StringId == id);
                list.Add(new ReignCastleSlotVM(room, hero, room == CastleRoom.GuestBedrooms ? () => Enter(room, id) : (Action)null));
            }
            return list;
        }

        private static MBBindingList<ReignCastleSlotVM> Slice(
            MBBindingList<ReignCastleSlotVM> source,
            int start,
            int count)
        {
            var row = new MBBindingList<ReignCastleSlotVM>();
            int end = Math.Min(source.Count, start + count);
            for (int i = start; i < end; i++) row.Add(source[i]);
            return row;
        }

        public static string RoomName(CastleRoom room) => room switch
        {
            CastleRoom.CastleGardens => "Castle Gardens", CastleRoom.NobleSolar => "Noble Solar",
            CastleRoom.Library => "Library", CastleRoom.TrainingYard => "Training Yard",
            CastleRoom.InnerCourtyard => "Inner Courtyard", CastleRoom.DiningChamber => "Small Dining Chamber",
            CastleRoom.StableCourtyard => "Stable Courtyard", CastleRoom.PortraitGallery => "Portrait Gallery",
            CastleRoom.MainHall => "Main Hall", CastleRoom.RoyalBedroom => "Royal Bedroom",
            CastleRoom.Chapel => "Chapel", CastleRoom.ThroneRoom => "Throne Room",
            CastleRoom.Baths => "Baths", CastleRoom.Battlements => "Battlements",
            _ => "Guest Bedrooms"
        };
        private static string TimeBlockName(int value) => ((CastleTimeBlock)value).ToString();
    }

    public sealed class ReignCastleSlotVM : ViewModel
    {
        private readonly Action _open;
        public ReignCastleSlotVM(CastleRoom room, Hero hero, Action open)
        {
            Room = (int)room; HeroStringId = hero?.StringId ?? string.Empty;
            PortraitCacheKey = hero == null ? string.Empty : CharacterCacheId.ForHero(hero) ?? string.Empty;
            Name = hero?.Name?.ToString() ?? string.Empty; _open = open;
        }
        public int Room { get; }
        public string HeroStringId { get; }
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public bool HasPortrait => !string.IsNullOrWhiteSpace(PortraitCacheKey);
        [DataSourceProperty] public bool CanOpen => _open != null && HasPortrait;
        public void ExecuteOpen() => _open?.Invoke();
    }
}
