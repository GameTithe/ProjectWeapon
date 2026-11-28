import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

const output = fileURLToPath(new URL('../Assets/Scenes/VfxRarityLab/Art/', import.meta.url));
const palette = [
  ['323746', '718193', 'C0C6CE', 'E8EBEF'],
  ['244442', '4C9B72', '75D594', 'D1F7BB'],
  ['233C65', '4387C0', '7ABFFF', 'D4EBFF'],
  ['3E2A61', '8E58AA', 'CF9EFA', 'F0D8FF'],
  ['583031', 'BA7140', 'FFD273', 'FFF2BD'],
];
const centers = [144, 232, 320, 408, 496];
const size = 88;
const frameCount = 5;

function rgba(hex, alpha = 255) {
  const code = Number.parseInt(hex, 16);
  return [code >> 16, (code >> 8) & 255, code & 255, alpha];
}

function canvas(w, h) {
  const data = Buffer.alloc(w * h * 4);
  const pixel = (x, y, color) => {
    x = Math.round(x);
    y = Math.round(y);
    if (x >= 0 && x < w && y >= 0 && y < h) data.set(color, (y * w + x) * 4);
  };
  const rect = (x, y, width, height, color) => {
    for (let j = y; j < y + height; j++) {
      for (let i = x; i < x + width; i++) pixel(i, j, color);
    }
  };
  const line = (x, y, endX, endY, color) => {
    const steps = Math.max(Math.abs(endX - x), Math.abs(endY - y), 1);
    for (let i = 0; i <= steps; i++) pixel(x + (endX - x) * i / steps, y + (endY - y) * i / steps, color);
  };
  return { w, h, data, pixel, rect, line };
}

const digits = {
  '0': ['111', '101', '101', '101', '111'], '1': ['010', '110', '010', '010', '111'],
  '2': ['111', '001', '111', '100', '111'], '3': ['111', '001', '111', '001', '111'],
  '4': ['101', '101', '111', '001', '001'], '5': ['111', '100', '111', '001', '111'],
  '6': ['111', '100', '111', '101', '111'], '7': ['111', '001', '010', '010', '010'],
  '8': ['111', '101', '111', '101', '111'], '9': ['111', '101', '111', '001', '111'],
  ':': ['000', '010', '000', '010', '000'],
  A: ['010', '101', '111', '101', '101'], B: ['110', '101', '110', '101', '110'],
  C: ['111', '100', '100', '100', '111'], D: ['110', '101', '101', '101', '110'],
  E: ['111', '100', '110', '100', '111'], F: ['111', '100', '110', '100', '100'],
  G: ['111', '100', '101', '101', '111'], H: ['101', '101', '111', '101', '101'],
  I: ['111', '010', '010', '010', '111'], L: ['100', '100', '100', '100', '111'],
  M: ['101', '111', '111', '101', '101'], N: ['101', '111', '111', '111', '101'],
  O: ['111', '101', '101', '101', '111'], P: ['111', '101', '111', '100', '100'],
  R: ['110', '101', '110', '101', '101'], S: ['111', '100', '111', '001', '111'],
  T: ['111', '010', '010', '010', '010'], U: ['101', '101', '101', '101', '111'],
  V: ['101', '101', '101', '101', '010'], W: ['101', '101', '111', '111', '101'],
  X: ['101', '101', '010', '101', '101'], Y: ['101', '101', '010', '010', '010'],
};

function text(surface, value, x, y, color, scale = 1) {
  for (const [index, char] of [...value].entries()) {
    const letter = digits[char];
    if (!letter) continue;
    letter.forEach((row, dy) => {
      for (let dx = 0; dx < row.length; dx++) {
        if (row[dx] === '1') surface.rect(x + (index * 4 + dx) * scale, y + dy * scale, scale, scale, color);
      }
    });
  }
}

function room() {
  const art = canvas(640, 288);
  const { pixel, rect, line } = art;
  rect(0, 0, 640, 288, rgba('161520'));
  const floor = ['363141', '3A3444', '3D3546', '393345', '403648', '433A4A'];
  for (let row = 0; row < 9; row++) {
    for (let col = -1; col < 21; col++) {
      const x = col * 32 + (row % 2) * 16;
      const y = row * 22 + 59;
      const seed = ((col + 30) * 107 + row * 43) % 17;
      rect(x + 1, y + 1, 31, 21, rgba(floor[seed % floor.length]));
      line(x + 2, y + 1, x + 30, y + 1, rgba('494050'));
      line(x + 2, y + 21, x + 31, y + 21, rgba('2E2B39'));
      if (seed % 3 === 0) {
        line(x + 15, y + 1, x + 14, y + 5, rgba('2E2937'));
        line(x + 14, y + 5, x + 19, y + 9, rgba('2E2937'));
        pixel(x + 22, y + 12, rgba('564654'));
      }
    }
  }
  rect(0, 0, 640, 63, rgba('211C2A'));
  for (let row = 0; row < 4; row++) {
    for (let col = -1; col < 21; col++) {
      const x = col * 34 + (row % 2) * 17;
      const y = row * 15 + 2;
      rect(x + 1, y, 33, 14, rgba(floor[((col + 8) * 3 + row) % 5]));
      line(x + 2, y + 1, x + 31, y + 1, rgba('4B4052'));
      line(x + 32, y + 2, x + 32, y + 12, rgba('2B2633'));
    }
  }
  rect(0, 59, 640, 6, rgba('1C1725'));
  rect(0, 59, 640, 1, rgba('524658'));
  for (const x of [49, 110, 189, 277, 365, 453, 537, 590]) {
    rect(x - 7, 0, 17, 74, rgba('241D2D'));
    rect(x - 7, 0, 12, 67, rgba('4E4358'));
    rect(x - 5, 1, 2, 63, rgba('635366'));
    rect(x + 4, 0, 3, 69, rgba('322B3C'));
    for (const y of [16, 32, 48]) rect(x - 7, y, 14, 1, rgba('312638'));
    rect(x - 10, 62, 19, 4, rgba('5C4D60'));
    rect(x - 9, 67, 20, 3, rgba('2B2435'));
  }
  for (const x of [96, 291, 350, 544]) {
    rect(x - 1, 56, 3, 11, rgba('926252'));
    rect(x - 4, 55, 9, 2, rgba('B98463'));
    line(x, 43, x - 2, 53, rgba('ECA063'));
    line(x, 46, x + 3, 54, rgba('BF6E55'));
    rect(x - 1, 49, 3, 5, rgba('FFBF78'));
    pixel(x + 3, 44, rgba('CC8665'));
  }

  // Chest on the left and the cyan portal on the right establish stable landmarks.
  rect(119, 79, 23, 12, rgba('221923'));
  rect(121, 75, 19, 14, rgba('704050'));
  rect(122, 75, 17, 3, rgba('A16754'));
  rect(120, 79, 21, 2, rgba('D49863'));
  rect(123, 82, 2, 6, rgba('AB6E56'));
  rect(135, 82, 2, 6, rgba('AB6E56'));
  rect(129, 80, 3, 4, rgba('F0BB74'));
  for (let y = 68; y < 103; y++) {
    for (let x = 504; x < 541; x++) {
      const r = ((x - 522) / 15) ** 2 + ((y - 86) / 17) ** 2;
      if (r > 1.13) continue;
      const stripe = (x + y * 2) % 9;
      pixel(x, y, rgba(r > 0.8 ? '281F38' : r > 0.62 ? '607397' : r > 0.4 ? '5187BA'
        : stripe < 2 ? '87D3DF' : '365786'));
    }
  }
  rect(506, 100, 33, 3, rgba('4C5878'));
  rect(509, 104, 26, 2, rgba('63799A'));

  // The repeated red enemy warnings sit behind all five temporary effects.
  for (const [index, x] of centers.entries()) {
    const y = 159;
    line(x + 1, y, x + 14, y - 10, rgba('A04D5C'));
    line(x + 14, y - 10, x + 35, y, rgba('BF626B'));
    line(x + 35, y, x + 21, y + 10, rgba('E57772'));
    line(x + 21, y + 10, x + 1, y, rgba('E57772'));
    line(x + 21, y + 9, x + 12, y + 4, rgba('70404E'));
    rect(x + 14, y - 35, 18, 3, rgba('221C2A'));
    rect(x + 15, y - 34, 16, 1, rgba('DA656E'));

    rect(x - 36, 193, 72, 28, rgba('201B29'));
    rect(x - 35, 193, 70, 1, rgba('57465A'));
    rect(x - 33, 215, 66, 1, rgba(palette[index][1]));
    text(art, String(index + 1), x - 27, 201, rgba(palette[index][2]), 2);
    const ranks = ['COMMON', 'UNCOMMON', 'RARE', 'EPIC', 'LEGENDARY'];
    text(art, ranks[index], x - 13, 204, rgba('D5C5C6'));
    for (let tick = 0; tick <= index; tick++) rect(x + 9 + tick * 4, 211, 2, 1, rgba(palette[index][2]));
  }
  rect(91, 21, 98, 22, rgba('1A1723'));
  rect(92, 21, 96, 1, rgba('88726F'));
  text(art, 'HP', 98, 27, rgba('DDCAC6'));
  rect(110, 27, 71, 5, rgba('673443'));
  rect(110, 27, 59, 5, rgba('D06569'));
  line(110, 27, 168, 27, rgba('F19486'));
  rect(110, 35, 71, 1, rgba('8A6570'));
  rect(468, 21, 80, 22, rgba('1A1723'));
  rect(469, 21, 78, 1, rgba('88726F'));
  text(art, '08:36', 511, 29, rgba('FFD273'));
  text(art, 'TIME', 480, 29, rgba('BEABAD'));
  rect(223, 237, 194, 25, rgba('211A29'));
  rect(224, 237, 192, 1, rgba('57465A'));
  text(art, 'SPACE REPLAY', 234, 247, rgba('DBC9C7'));
  rect(292, 244, 1, 10, rgba('594A5B'));
  text(art, 'R REDUCED', 301, 247, rgba('DBC9C7'));
  rect(348, 244, 1, 10, rgba('594A5B'));
  text(art, '1 5 SOLO', 361, 247, rgba('DBC9C7'));
  return art;
}

function dummy() {
  const art = canvas(26, 34);
  const { rect, line, pixel } = art;
  rect(4, 29, 19, 3, rgba('241E2D'));
  rect(7, 12, 14, 17, rgba('241F2D'));
  rect(8, 13, 12, 10, rgba('51465B'));
  rect(9, 14, 9, 5, rgba('786779'));
  rect(9, 19, 8, 6, rgba('3D3446'));
  rect(8, 23, 4, 7, rgba('3A303E'));
  rect(15, 23, 5, 7, rgba('4E4050'));
  rect(7, 28, 6, 2, rgba('7B625F'));
  rect(15, 28, 6, 2, rgba('6D595D'));
  rect(9, 4, 11, 10, rgba('302737'));
  rect(10, 3, 8, 2, rgba('695365'));
  rect(10, 5, 9, 7, rgba('B39DA4'));
  rect(11, 7, 8, 3, rgba('574A62'));
  rect(11, 8, 3, 1, rgba('E8B7A1'));
  pixel(17, 8, rgba('E8B7A1'));
  rect(12, 10, 6, 2, rgba('837187'));
  rect(6, 12, 5, 4, rgba('AA5E62'));
  line(9, 15, 5, 22, rgba('883F54'));
  rect(4, 19, 3, 5, rgba('693348'));
  line(22, 13, 17, 27, rgba('D8C4BE'));
  line(23, 12, 19, 26, rgba('8D7E8C'));
  line(17, 23, 23, 25, rgba('936C64'));
  return art;
}

function attackFrames() {
  const atlas = canvas(size * frameCount * 2, size * 5);
  const phase = [
    { start: -1.52, end: -0.28, r: 0.77, alpha: 180 },
    { start: -1.38, end: 0.79, r: 0.94, alpha: 255 },
    { start: -1.26, end: 1.25, r: 1.03, alpha: 255 },
    { start: -0.80, end: 1.40, r: 1.10, alpha: 160 },
    { start: -0.30, end: 1.29, r: 1.13, alpha: 90 },
  ];
  const baseRadius = [25, 27, 29, 31, 33];
  const thickness = [3, 4, 5, 6, 7];
  for (let mode = 0; mode < 2; mode++) {
    for (let grade = 0; grade < 5; grade++) {
      for (let frame = 0; frame < frameCount; frame++) {
        const tuning = phase[frame];
        const rad = baseRadius[grade] * tuning.r;
        const ox = size * (frame + frameCount * mode);
        const oy = size * grade;
        const stroke = thickness[grade];
        const shades = palette[grade];
        const set = (x, y, shade, alpha) => atlas.pixel(ox + x, oy + y, rgba(shades[shade], alpha));
        for (let y = 1; y < size - 1; y++) {
          for (let x = 1; x < size - 1; x++) {
            const dx = x - 35;
            const dy = (y - 42) * 1.13;
            const angle = Math.atan2(dy, dx);
            if (angle < tuning.start || angle > tuning.end) continue;
            const progress = (angle - tuning.start) / (tuning.end - tuning.start);
            const taper = Math.sin(progress * Math.PI) ** 0.57;
            const radius = Math.hypot(dx, dy);
            const inside = rad - radius;
            const width = Math.max(1, taper * stroke);
            if (inside >= -1 && inside <= width + 1) {
              const shade = inside < 0 ? 0 : inside < 1.35 ? 2 : inside < 2.7 ? 3 : inside < width - 0.65 ? 1 : 0;
              set(x, y, shade, tuning.alpha);
            } else if ((mode === 0 || grade < 3) && grade >= 2 && progress > 0.14 && progress < 0.83) {
              const echo = 3 + (grade - 1) * 2;
              if (inside >= width + echo && inside < width + echo + (grade >= 3 ? 2.1 : 1.1)
                && (x + y + grade) % (grade === 4 ? 7 : 9) !== 0) set(x, y, grade >= 3 ? 1 : 0, tuning.alpha * 0.66);
            }
          }
        }
        const sparks = (mode === 1 && grade >= 3) ? grade + 1 : 2 + grade * 3;
        for (let spark = 0; spark < sparks; spark++) {
          const band = (spark * 37 + grade * 11) % 101 / 101;
          const angle = tuning.start + band * (tuning.end - tuning.start);
          const offset = 2 + ((spark * 5 + frame * 3) % 11) + frame * 1.05;
          const length = rad + offset;
          const x = Math.round(35 + Math.cos(angle) * length);
          const y = Math.round(42 + Math.sin(angle) * length / 1.15);
          if (x < 2 || x > size - 3 || y < 2 || y > size - 3) continue;
          set(x, y, spark % 3 === 0 ? 3 : 2, tuning.alpha * 0.84);
          if (grade >= 2 && spark % 2 === 0) {
            set(x - 1, y, 1, tuning.alpha);
            set(x, y + 1, 1, tuning.alpha);
          }
        }
        if (mode === 0 && grade >= 3 && (frame === 1 || frame === 2)) {
          const x = Math.round(35 + rad - 2);
          const y = frame === 1 ? 39 : 48;
          const sizeOfFlare = grade === 4 ? 6 : 4;
          for (let i = -sizeOfFlare; i <= sizeOfFlare; i++) {
            set(x + i, y, Math.abs(i) < 3 ? 3 : 2, tuning.alpha);
            set(x, y + i, Math.abs(i) < 2 ? 3 : 2, tuning.alpha);
          }
        }
      }
    }
  }
  return atlas;
}

function crc32(bytes) {
  let crc = -1;
  for (const value of bytes) {
    crc ^= value;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  return (crc ^ -1) >>> 0;
}

function chunk(name, data) {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(data.length);
  head.write(name, 4);
  const checksum = Buffer.alloc(4);
  checksum.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])));
  return Buffer.concat([head, data, checksum]);
}

function png(name, image) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(image.w);
  header.writeUInt32BE(image.h, 4);
  header.set([8, 6], 8);
  const stride = image.w * 4 + 1;
  const raw = Buffer.alloc(stride * image.h);
  for (let y = 0; y < image.h; y++) image.data.copy(raw, y * stride + 1, y * (stride - 1), (y + 1) * (stride - 1));
  const file = join(output, name);
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', header), chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]));
}

png('ComparisonRoom.png', room());
png('TrainingTarget.png', dummy());
png('RaritySlashFrames.png', attackFrames());
