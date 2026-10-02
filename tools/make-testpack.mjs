/** 造一个 1.12 风格的测试资源包，用于验收 UI */
import * as fs from "node:fs";
import * as path from "node:path";

const out = process.argv[2];
if (!out) { console.error("用法: make-testpack.mjs <输出目录>"); process.exit(1); }

fs.rmSync(out, { recursive: true, force: true });

/* 1x1 透明 PNG */
const TINY_PNG = Buffer.from([
  0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
  0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
  0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
  0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
  0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,
  0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
  0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,
  0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,
  0x42,0x60,0x82
]);

const w = (rel, data) => {
  const full = path.join(out, rel);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, data);
};

/* pack.mcmeta —— 1.12 的格式 */
w("pack.mcmeta", JSON.stringify({
  pack: {
    pack_format: 3,
    description: "§6我的 1.12 材质包\n§7需要转换到新版本"
  }
}, null, 2));

/* 旧版复数目录 —— 这是转换时必须迁移的部分 */
w("assets/minecraft/textures/blocks/stone.png", TINY_PNG);
w("assets/minecraft/textures/blocks/dirt.png", TINY_PNG);
w("assets/minecraft/textures/blocks/grass_top.png", TINY_PNG);
w("assets/minecraft/textures/blocks/oak_planks.png", TINY_PNG);
w("assets/minecraft/textures/blocks/glass.png", TINY_PNG);
w("assets/minecraft/textures/items/apple.png", TINY_PNG);
w("assets/minecraft/textures/items/diamond_sword.png", TINY_PNG);
w("assets/minecraft/textures/items/stick.png", TINY_PNG);

/* 保持不变的目录 */
w("assets/minecraft/textures/gui/widgets.png", TINY_PNG);
w("assets/minecraft/textures/entity/steve.png", TINY_PNG);

/* 自定义命名空间 */
w("assets/tutorial/textures/blocks/ruby_ore.png", TINY_PNG);
w("assets/tutorial/textures/items/ruby.png", TINY_PNG);

/* 模型 */
w("assets/minecraft/models/block/stone.json", JSON.stringify({
  parent: "block/cube_all",
  textures: { all: "blocks/stone" }
}, null, 2));
w("assets/minecraft/models/item/apple.json", JSON.stringify({
  parent: "item/generated",
  textures: { layer0: "items/apple" }
}, null, 2));

/* 旧版 .lang 语言文件 —— 键名需要迁移 */
w("assets/minecraft/lang/en_US.lang", [
  "block.stone=Stone",
  "block.dirt=Dirt",
  "block.grass=Grass Block",
  "block.oak_planks=Oak Planks",
  "block.glass=Glass",
  "tile.chest=Chest",
  "item.apple=Apple",
  "item.diamond_sword=Diamond Sword",
  "item.stick=Stick",
  "itemGroup.buildingBlocks=Building Blocks",
  "gui.done=Done",
  "menu.singleplayer=Singleplayer"
].join("\n"));

w("assets/minecraft/lang/zh_CN.lang", [
  "block.stone=石头",
  "block.dirt=泥土",
  "block.grass=草方块",
  "item.apple=苹果",
  "item.diamond_sword=钻石剑",
  "gui.done=完成"
].join("\n"));

/* 音效 */
w("assets/minecraft/sounds.json", JSON.stringify({
  "block.stone.break": { category: "block", sounds: [{ name: "dig/stone", stream: false }] }
}, null, 2));
w("assets/minecraft/sounds/dig/stone1.ogg", Buffer.from("OggS-placeholder"));

/* 一个稍大的文件，用于验证字节完整性 */
const big = Buffer.alloc(4096);
for (let i = 0; i < big.length; i++) big[i] = (i * 7) & 0xFF;
w("assets/minecraft/textures/blocks/large_test.png", big);

/* 统计 */
let files = 0, bytes = 0;
const walk = (d) => {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const f = path.join(d, e.name);
    if (e.isDirectory()) walk(f);
    else { files++; bytes += fs.statSync(f).size; }
  }
};
walk(out);

console.log(`测试资源包已生成: ${out}`);
console.log(`  ${files} 个文件, ${(bytes / 1024).toFixed(1)} KB`);
console.log("  结构: pack_format 3 (1.12) + textures/blocks + textures/items + en_US.lang");
