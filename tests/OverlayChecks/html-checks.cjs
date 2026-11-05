const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const root = process.argv[2] ? path.resolve(process.argv[2]) : path.resolve(__dirname, "../..");
const context = vm.createContext({ document: {}, $: () => ({ ready() {} }) });
vm.runInContext(fs.readFileSync(path.join(root, "html/main.js"), "utf8"), context);
for (const civ of ["vikings", "scots", "jin_dynasty", "zhu_xis_legacy", "Jeanne d’Arc"]) {
    const html = context.civ_flag_img(civ, "aoe4");
    const src = html.match(/src="([^"]+)"/);
    assert.ok(src, `${civ} renders a flag image`);
    assert.ok(fs.existsSync(path.resolve(root, "html", decodeURI(src[1]))), `${civ} image exists locally`);
}
const flags = vm.runInContext("AOE4_FLAGS", context);
assert.equal(new Set(Object.values(flags)).size, 25, "all 25 released AOE4 civilization flags registered");
for (const file of Object.values(flags)) assert.ok(fs.existsSync(path.join(root, "img/flags", file)), `${file} exists`);
assert.ok(context.civ_flag_img("vikings", "aoe4", "维京人").includes('title="维京人"'), "Chinese display name reaches HTML flag tooltip");
assert.ok(context.civ_flag_img("scots", "aoe4", "Scots").includes('alt="Scots"'), "English display name reaches HTML image label");
assert.ok(context.civ_flag_img("scots", "aoe4", 'test"<').includes("test&quot;&lt;"), "flag labels escape HTML attributes");
console.log("PASS HTML civilization flags, DLC names and normalized aliases");
