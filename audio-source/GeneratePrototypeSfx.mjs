import { mkdirSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SAMPLE_RATE = 24_000;
const TAU = 2 * Math.PI;
const output = process.argv[2]
  ? resolve(process.argv[2])
  : fileURLToPath(new URL("../Assets/ProjectWeapon/PrototypeAudio/Clips/", import.meta.url));

const tone = (frequency, time) => Math.sin(TAU * frequency * time);

function chirp(start, end, duration, time) {
  return Math.sin(TAU * (start * time + (end - start) * time * time / (2 * duration)));
}

function envelope(time, duration, attack, power) {
  if (time < 0 || time >= duration) return 0;
  return Math.min(1, time / attack) * Math.pow(Math.max(0, 1 - time / duration), power);
}

function note(frequency, time, duration) {
  const shape = envelope(time, duration, 0.006, 2);
  return shape * (tone(frequency, time) + 0.23 * tone(frequency * 2.01, time));
}

function generateNoise(seed) {
  let state = seed;
  let smooth = 0;
  return () => {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    const raw = (state >>> 0) / 0x80000000 - 1;
    smooth = 0.66 * smooth + 0.34 * raw;
    return { raw, smooth };
  };
}

function attack(time, noise) {
  const duration = 0.108;
  const shape = envelope(time, duration, 0.004, 1.8);
  return shape * (0.70 * noise.smooth + 0.43 * chirp(1700, 410, duration, time) + 0.13 * tone(2220, time));
}

function enemyHit(time, noise) {
  const duration = 0.078;
  const shape = envelope(time, duration, 0.002, 2.5);
  return shape * (0.62 * tone(430, time) + 0.25 * tone(1010, time) + 0.27 * noise.raw);
}

function playerHit(time, noise) {
  const duration = 0.145;
  const shape = envelope(time, duration, 0.005, 1.9);
  return shape * (0.85 * chirp(188, 78, duration, time) + 0.26 * tone(122, time) + 0.13 * noise.smooth);
}

function dodge(time, noise) {
  const duration = 0.148;
  const shape = Math.pow(Math.sin(Math.PI * time / duration), 2);
  return shape * (0.83 * noise.smooth + 0.15 * noise.raw + 0.27 * chirp(430, 1080, duration, time));
}

function pickup(time) {
  return 0.8 * note(620, time, 0.095) + note(930, time - 0.047, 0.11);
}

function extractionLoop(time) {
  const duration = 0.8;
  const phase = time / duration;
  const edge = Math.pow(Math.sin(Math.PI * phase), 2);
  const pulse = 0.40 + 0.60 * Math.pow(Math.sin(TAU * phase), 2);
  return edge * pulse * (tone(125, time) + 0.32 * tone(187.5, time) + 0.14 * tone(250, time));
}

function extractionSuccess(time) {
  return 0.70 * note(523.25, time, 0.18)
    + 0.77 * note(659.25, time - 0.045, 0.20)
    + note(783.99, time - 0.092, 0.21);
}

const clips = [
  ["WeaponAttack", 0.108, 0.18, attack],
  ["EnemyHit", 0.078, 0.19, enemyHit],
  ["PlayerHit", 0.145, 0.22, playerHit],
  ["Dodge", 0.148, 0.16, dodge],
  ["Pickup", 0.157, 0.18, pickup],
  ["ExtractionLoop", 0.8, 0.09, extractionLoop],
  ["ExtractionSuccess", 0.302, 0.22, extractionSuccess],
];

function writePcmWav(path, waveform, peak) {
  let largest = 0;
  for (const sample of waveform) largest = Math.max(largest, Math.abs(sample));

  const wav = Buffer.alloc(44 + waveform.length * 2);
  wav.write("RIFF", 0);
  wav.writeUInt32LE(wav.length - 8, 4);
  wav.write("WAVEfmt ", 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(SAMPLE_RATE, 24);
  wav.writeUInt32LE(SAMPLE_RATE * 2, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write("data", 36);
  wav.writeUInt32LE(waveform.length * 2, 40);

  for (let index = 0; index < waveform.length; index++) {
    wav.writeInt16LE(Math.round(waveform[index] / largest * peak * 32767), 44 + index * 2);
  }
  writeFileSync(path, wav);
}

mkdirSync(output, { recursive: true });
for (const [index, [name, duration, peak, synthesize]] of clips.entries()) {
  const noise = generateNoise(0x326fb041 + index);
  const waveform = new Float64Array(Math.round(duration * SAMPLE_RATE));
  for (let sample = 0; sample < waveform.length; sample++) {
    waveform[sample] = synthesize(sample / SAMPLE_RATE, noise());
  }
  writePcmWav(join(output, name + ".wav"), waveform, peak);
}
