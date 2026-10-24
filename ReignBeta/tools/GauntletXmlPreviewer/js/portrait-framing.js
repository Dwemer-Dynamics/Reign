// Metadata from the shipped portrait manifests. The portrait contract checks
// every fixture against its source so browser and native framing cannot drift.
export const PORTRAIT_FOCUS = {
  "Mengus (lord_5_7)": {
    "width": 1086,
    "height": 1448,
    "x": 0.42073938250541687,
    "y": 0.05096178129315376,
    "faceWidth": 0.10248568654060364,
    "faceHeight": 0.109152402728796
  },
  "Ira (lord_1_37)": {
    "width": 1086,
    "height": 1448,
    "x": 0.42852783203125,
    "y": 0.059311434626579285,
    "faceWidth": 0.08544176816940308,
    "faceHeight": 0.08974680304527283
  },
  "Corein (lord_5_10)": {
    "width": 1086,
    "height": 1448,
    "x": 0.4358188211917877,
    "y": 0.06376513838768005,
    "faceWidth": 0.08722159266471863,
    "faceHeight": 0.09789794683456421
  },
  "Caladog (lord_5_1)": {
    "width": 1086,
    "height": 1448,
    "x": 0.4342861771583557,
    "y": 0.0562078133225441,
    "faceWidth": 0.09001249074935913,
    "faceHeight": 0.0974031314253807
  },
  "Rhagaea (lord_1_14)": {
    "width": 1152,
    "height": 2048,
    "x": 0.43494924902915955,
    "y": 0.05228843912482262,
    "faceWidth": 0.12274298071861267,
    "faceHeight": 0.09764974191784859
  },
  "Derthert (lord_4_1)": {
    "width": 1086,
    "height": 1448,
    "x": 0.4333494305610657,
    "y": 0.047084905207157135,
    "faceWidth": 0.10058861970901489,
    "faceHeight": 0.1064777597784996
  },
  "Abagai (lord_6_12)": {
    "width": 1086,
    "height": 1448,
    "x": 0.4340313971042633,
    "y": 0.057842377573251724,
    "faceWidth": 0.0917990505695343,
    "faceHeight": 0.09038081392645836
  },
  "Aeron (lord_5_16)": {
    "width": 1086,
    "height": 1448,
    "x": 0.4272548258304596,
    "y": 0.05223849043250084,
    "faceWidth": 0.10400226712226868,
    "faceHeight": 0.10784964635968208
  },
  "Adalindis (lord_4_28_1)": {
    "width": 1086,
    "height": 1448,
    "x": 0.42290836572647095,
    "y": 0.06123979762196541,
    "faceWidth": 0.09126132726669312,
    "faceHeight": 0.09872682020068169
  },
  "Lucon (lord_1_1)": {
    "width": 1086,
    "height": 1448,
    "x": 0.42637497186660767,
    "y": 0.05811614915728569,
    "faceWidth": 0.10359752178192139,
    "faceHeight": 0.09715140238404274
  }
};

// Mirrors the source-linked ReignPortraitBustFraming policy; this changes only
// display framing and never writes a portrait or derivative.
export function framedBustCrop(width, height, aspect, focus) {
  if (!(width > 0 && height > 0 && Number.isFinite(aspect) && aspect > 0)) throw new Error("Invalid portrait geometry");
  const { x = -1, y = -1, faceWidth = 0, faceHeight = 0 } = focus || {};
  const focused = [x,y,faceWidth,faceHeight].every(Number.isFinite) && x >= 0 && y >= 0 && faceWidth > 0 && faceHeight > 0 && x + faceWidth <= 1.0001 && y + faceHeight <= 1.0001;
  const round = value => Math.sign(value) * Math.floor(Math.abs(value) + .5);
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));
  const facePixels = focused ? faceHeight * height : 0;
  const cropHeight = Math.max(1, Math.min(height, focused ? round(facePixels * 3.6) : height, Math.max(1, Math.floor(width / aspect))));
  const cropWidth = Math.max(1, Math.min(width, round(cropHeight * aspect)));
  const centerX = focused ? (x + faceWidth * .5) * width : width * .5;
  return { x: clamp(round(centerX - cropWidth * .5), 0, width - cropWidth), y: focused ? clamp(round(y * height - facePixels * .85), 0, height - cropHeight) : 0, width: cropWidth, height: cropHeight };
}

export function applyFramedBustPreview(element, asset, width, height) {
  const folder = asset.match(/\/PortraitCache\/_shared\/([^/]+)\/portrait\.png$/)?.[1];
  const focus = PORTRAIT_FOCUS[folder];
  if (!focus) throw new Error("Missing full-head portrait preview metadata: " + asset);
  const crop = framedBustCrop(focus.width, focus.height, width / height, focus);
  const scale = Math.max(width / crop.width, height / crop.height);
  element.style.backgroundSize = `${focus.width * scale}px ${focus.height * scale}px`;
  element.style.backgroundPosition = `${-crop.x * scale}px ${-crop.y * scale}px`;
  element.style.backgroundRepeat = "no-repeat";
  element.dataset.framedBustCrop = JSON.stringify(crop);
}

