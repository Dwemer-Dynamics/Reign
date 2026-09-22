const ambassadors = [
  { kingdom: "Western Empire", name: "Mitela", clan: "Clan Comnos", portrait: "assets/portraits/ambassador-1.png", mark: "♜", color: "#6a2431", ink: "#e5c069" },
  { kingdom: "Khuzait Khanate", name: "Abagai", clan: "Clan Khergit", portrait: "assets/portraits/ambassador-2.png", mark: "✦", color: "#26746e", ink: "#e2d7aa" },
  { kingdom: "Northern Empire", name: "Abalytos", clan: "House Leonipardes", portrait: "assets/portraits/ambassador-3.png", mark: "♚", color: "#5b3b75", ink: "#e7c96d" },
  { kingdom: "Khuzait Khanate", name: "Achaku", clan: "Clan Baltait", portrait: "assets/portraits/ambassador-4.png", mark: "☀", color: "#2c7e75", ink: "#f3d773" },
  { kingdom: "Southern Empire", name: "Achios", clan: "House Pethros", portrait: "assets/portraits/ambassador-5.png", mark: "♛", color: "#532174", ink: "#e6b62d" },
  { kingdom: "Vlandia", name: "Adalindis", clan: "House dey Meroc", portrait: "assets/portraits/ambassador-6.png", mark: "♜", color: "#7c2227", ink: "#dbb34d" },
  { kingdom: "Vlandia", name: "Adaltrud", clan: "House dey Tihr", portrait: "assets/portraits/ambassador-7.png", mark: "♜", color: "#7c2227", ink: "#dbb34d" },
  { kingdom: "Aserai", name: "Addas", clan: "Banu Sarran", portrait: "assets/portraits/ambassador-8.png", mark: "✦", color: "#89722a", ink: "#15130e" }
];

const visibleCards = 5;
const rail = document.querySelector("#ambassadorRail");
const pageDots = document.querySelector("#pageDots");
let offset = 0;
let selectedIndex = 0;

function renderCards() {
  rail.replaceChildren(...ambassadors.map((ambassador, index) => {
    const card = document.createElement("button");
    card.type = "button";
    card.className = "ambassador-card";
    card.dataset.index = index;
    card.setAttribute("aria-label", `${ambassador.kingdom} ambassador ${ambassador.name} of ${ambassador.clan}`);
    card.style.setProperty("--banner-color", ambassador.color);
    card.style.setProperty("--banner-mark", ambassador.ink);

    const banner = document.createElement("span");
    banner.className = "ambassador-card__banner";
    banner.dataset.bannerBinding = `Ambassadors[${index}].LeadingClanBanner`;

    const bannerField = document.createElement("span");
    bannerField.className = "ambassador-card__banner-field";
    bannerField.dataset.mark = ambassador.mark;

    const bannerOverlay = document.createElement("span");
    bannerOverlay.className = "ambassador-card__banner-overlay";
    banner.append(bannerField, bannerOverlay);

    const portrait = document.createElement("img");
    portrait.className = "ambassador-card__portrait";
    portrait.src = ambassador.portrait;
    portrait.alt = "";
    portrait.draggable = false;
    portrait.dataset.portraitBinding = `Ambassadors[${index}].FullBodyPortrait`;
    portrait.dataset.portraitFile = "portrait.png";
    portrait.title = "Runtime slot: cached NPC portrait.png";

    const portraitOverlay = document.createElement("span");
    portraitOverlay.className = "ambassador-card__portrait-overlay";

    const kingdom = document.createElement("span");
    kingdom.className = "ambassador-card__kingdom";
    kingdom.textContent = ambassador.kingdom;

    const name = document.createElement("span");
    name.className = "ambassador-card__name";
    name.textContent = ambassador.name;

    const clan = document.createElement("span");
    clan.className = "ambassador-card__clan";
    clan.textContent = ambassador.clan;

    card.append(portrait, portraitOverlay, banner, kingdom, name, clan);
    card.addEventListener("click", () => selectAmbassador(index));
    return card;
  }));
}

function renderDots() {
  const pages = ambassadors.length - visibleCards + 1;
  pageDots.replaceChildren(...Array.from({ length: pages }, (_, index) => {
    const dot = document.createElement("i");
    dot.classList.toggle("is-active", index === offset);
    return dot;
  }));
}

function updateRail() {
  const gapPercent = 0.75;
  const cardPercent = (100 - (4 * gapPercent)) / 5;
  rail.style.transform = `translateX(-${offset * (cardPercent + gapPercent)}%)`;
  renderDots();
}

function selectAmbassador(index) {
  selectedIndex = index;
  document.querySelectorAll(".ambassador-card").forEach((card, cardIndex) => {
    card.classList.toggle("is-selected", cardIndex === selectedIndex);
  });
}

function scrollRail(direction) {
  offset = Math.max(0, Math.min(ambassadors.length - visibleCards, offset + direction));
  updateRail();
}

document.querySelector(".hotspot--previous").addEventListener("click", () => scrollRail(-1));
document.querySelector(".hotspot--next").addEventListener("click", () => scrollRail(1));

// Reserved prototype hotspots. Their feature hooks will be connected in a later pass.
document.querySelector(".hotspot--close").addEventListener("click", () => {});
document.querySelector(".action--establish").addEventListener("click", () => {});
document.querySelector(".action--remove").addEventListener("click", () => {});

renderCards();
selectAmbassador(0);
updateRail();
