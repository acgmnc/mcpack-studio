# NBT 测试样本

这几个文件是**合成**的（不是真实存档），专门用来手工验证 NBT 模块的读写。
由 `NbtIo` 自己写出、再自己读回校验过，所以它们同时也是「写出去的格式能被读回来」的证据。

| 文件 | 格式 | 用途 |
| --- | --- | --- |
| `sample_java_level.dat` | gzip 压缩 · 大端（Java 版存档的常见形态） | 验证树形浏览、编辑、查找替换、另存 |
| `sample_bedrock_level.dat` | 不压缩 · 小端 · 带基岩版 8 字节头 | 验证小端与基岩版头的识别 |
| `sample.snbt` | SNBT 文本 | 验证「SNBT 与 JSON」页的文本导入 |
| `sample.json` | JSON 导出结果 | 对照导出内容 |

## 里面有什么

Java 样本刻意覆盖了全部 13 种标签类型和一些容易踩坑的值：

- 标量：`version`（Int）、`LastPlayed`（Long）、`health`（Float）、`Pos`（Double 列表）
- 字符串：中文（`LevelName`）、带命名空间 ID 的英文
- 数组：`RandomBytes`（Byte_Array）、`BiomeSeed`（Int_Array）、`ChunkKeys`（Long_Array，含 `long.MinValue` 一类的极值）
- 嵌套：`Player.Inventory` 是复合标签列表，其中一件物品还带 `Enchantments` 子列表
- 空列表：`EmptyList`（按规范元素类型是 `TAG_End`）
- 基岩版样本：额外带 `lastOpenedWithVersion`（Int 列表），数值全部按小端存

## 怎么用

图形界面里「NBT · NBT 编辑器」→ 选文件；或直接：

```
MCPackStudio.exe --nbt testnbt\sample_java_level.dat
```

改完记得用「另存为…」写到别处，别把样本覆盖了（覆盖了也能用自测里的 `NbtIo` 往返逻辑重新生成）。
