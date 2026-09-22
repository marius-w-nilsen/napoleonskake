// Generates placeholder Teams app icons without any dependencies:
//   appPackage/color.png   192x192, a stack of Napoleonskake layers
//   appPackage/outline.png  32x32, white-on-transparent layer outline
// Run: node scripts/make-icons.mjs
import { writeFileSync } from "node:fs";
import { deflateSync, crc32 } from "node:zlib";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const outDir = join(dirname(fileURLToPath(import.meta.url)), "..", "appPackage");

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const typeBuf = Buffer.from(type, "ascii");
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])) >>> 0);
  return Buffer.concat([len, typeBuf, data, crc]);
}

function png(size, pixel) {
  const raw = Buffer.alloc((size * 4 + 1) * size);
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0; // filter: none
    for (let x = 0; x < size; x++) {
      const [r, g, b, a] = pixel(x, y);
      raw.set([r, g, b, a], y * (size * 4 + 1) + 1 + x * 4);
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

// Napoleonskake: pastry, custard, pastry, custard, pastry, icing with a zig-zag of chocolate.
const PASTRY = [212, 160, 88, 255];
const CUSTARD = [250, 224, 140, 255];
const ICING = [255, 250, 240, 255];
const CHOCOLATE = [92, 52, 30, 255];
const BACKGROUND = [243, 217, 164, 255];

function colorIcon(size) {
  const margin = Math.round(size * 0.18);
  const left = margin, right = size - margin, top = Math.round(size * 0.26), bottom = size - margin;
  const height = bottom - top;
  const layers = [ICING, CUSTARD, PASTRY, CUSTARD, PASTRY, CUSTARD, PASTRY]; // top to bottom
  return png(size, (x, y) => {
    if (x < left || x >= right || y < top || y >= bottom) return BACKGROUND;
    const band = Math.min(layers.length - 1, Math.floor(((y - top) / height) * layers.length));
    if (band === 0) {
      // chocolate feathering on the icing
      const wave = Math.round(size * 0.02) + Math.round(Math.abs(Math.sin((x / size) * Math.PI * 6)) * size * 0.05);
      if (y - top >= wave && y - top < wave + Math.max(2, Math.round(size * 0.015))) return CHOCOLATE;
    }
    return layers[band];
  });
}

function outlineIcon(size) {
  const margin = Math.round(size * 0.12);
  const left = margin, right = size - margin, top = Math.round(size * 0.28), bottom = size - margin;
  const stroke = Math.max(1, Math.round(size / 16));
  const bands = 3;
  return png(size, (x, y) => {
    const inside = x >= left && x < right && y >= top && y < bottom;
    if (!inside) return [0, 0, 0, 0];
    const onFrame = x < left + stroke || x >= right - stroke || y < top + stroke || y >= bottom - stroke;
    const bandHeight = (bottom - top) / bands;
    const onBand = ((y - top) % bandHeight) < stroke && y > top + stroke;
    return onFrame || onBand ? [255, 255, 255, 255] : [0, 0, 0, 0];
  });
}

writeFileSync(join(outDir, "color.png"), colorIcon(192));
writeFileSync(join(outDir, "outline.png"), outlineIcon(32));
console.log("Wrote appPackage/color.png and appPackage/outline.png");
