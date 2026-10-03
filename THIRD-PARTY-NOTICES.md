# 第三方许可声明

本项目包含从下列开源项目移植（port）而来的功能实现。按各自许可证的要求，
在此保留原始版权声明与许可条款全文。

---

## NBTExplorer

- 上游：https://github.com/jaquadro/NBTExplorer
- 版权：Copyright (C) 2011 by Justin Aquadro
- 许可证：MIT

本项目的 NBT 模块（`src/NbtTag.cs`、`src/NbtIo.cs`、`src/NbtSnbt.cs`、`src/NbtEdit.cs`、
`src/NbtPage.cs`、`src/NbtSnbtPage.cs`、`src/NbtDialogs.cs`）是按其 Windows 版的行为用 C# 重写的，
没有引用它的任何二进制或源码文件。移植的能力包括：树形浏览 NBT 数据、增删改标签、
剪切/复制/粘贴、上下移动、查找与替换、SNBT / JSON 导出、字节数组十六进制编辑、撤销重做。

```
Copyright (C) 2011 by Justin Aquadro

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

---

## Substrate

- 上游：https://github.com/jaquadro/Substrate
- 版权：Copyright (C) 2011 by Justin Aquadro
- 许可证：MIT（条款同上）

参考它实现的 NBT 二进制格式处理细节：标签类型的编号、复合标签的 `TAG_End` 结束约定、
列表元素不带名字、字符串的 2 字节长度前缀，以及非官方的 `TAG_Short_Array(13)` 扩展。

---

## 其它数据来源

- 版本对照表数据来自 [Little100/Minecraft-Resourcespack-Editor](https://github.com/Little100/Minecraft-Resourcespack-Editor)（GPL-3.0），
  本项目整体以 GPL-3.0 发布，完整许可证见 [LICENSE](LICENSE)。
