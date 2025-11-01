# TrayChrome - 托盘浏览器

一个基于 WPF 和 WebView2 的轻量级托盘浏览器，专为 Windows 设计。类似 MenubarX，但更加轻便和功能丰富。
[这里](https://abstracted-aragon-945.notion.site/28965c515f2a80fdaac0d31063436139?v=28965c515f2a8008bc0f000c544792c5&pvs=74)是一些建议网站，适合traychrome

<img width="1536" height="864" alt="image" src="https://github.com/user-attachments/assets/580e94d7-baa8-4b27-9025-1b3205ab0b65" />

left click drag , right click resize.
![PixPin_2025-09-16_00-23-42](https://github.com/user-attachments/assets/bdcb9626-7e16-4dc2-b085-0f298e28353b)

and you can have mutiple of it.
![PixPin_2025-09-16_00-24-16](https://github.com/user-attachments/assets/f4532983-d1fb-4ff5-bed4-042e2966772b)

## ✨ 主要特性

- 🎯 **托盘集成**：最小化到系统托盘，不占用任务栏空间
- 📱 **移动优先**：默认使用移动端 User-Agent，适合移动版网站
- 🎨 **超级极简模式**：隐藏工具栏，纯净浏览体验
- 🔄 **多实例支持**：可同时运行多个独立的浏览器实例
- 🌙 **暗色模式**：支持网页暗色模式切换
- 📌 **窗口置顶**：可设置窗口始终保持在最前面
- ⭐ **收藏夹管理**：本地收藏夹存储和管理

## 🚀 启动参数

### 基本用法
双击exe启动

### 快捷键说明滚

#### 快捷键说明

| 快捷键                     | 功能                         |
|---------------------------|------------------------------|
| `中键`                | 关闭 TrayChrome 实例          |
| `Ctrl + 中键`                | 关闭当前实例                  |
| `Ctrl + L`                | 聚焦到地址栏      |
| `Ctrl + R`                | 刷新当前页面                  |
| `Ctrl +Shift + O`      | 书签                   |
| `Ctrl +Shift + B`                 | 切换极简模式（Clean 模式）  |
| `Ctrl + 点击链接` |在新窗口中打开





### 参数说明
```bash
.\TrayChrome.exe [参数]
```

| 参数 | 功能 | 示例 |
|------|------|------|
| `--url <网址>` | 指定启动时加载的网页 | `--url "https://www.google.com"` |
| `--open` | 启动时直接显示窗口 | `--open` |
| `--clean` | 启用超级极简模式（隐藏工具栏） | `--clean` |
| `--unclean` | 强制禁用超级极简模式 | `--unclean` |
| `--help` 或 `-h` | 显示帮助信息 | `--help` |
| `--size <宽度x高度>` | 指定窗口大小 | `--size 800x600` |

### 使用示例

```bash
# 启动极简模式jandan.net
.\TrayChrome.exe --url "https://jandan.net" --open --clean

# 强制禁用极简模式启动
.\TrayChrome.exe --unclean

# 显示帮助信息
.\TrayChrome.exe --help

# 直接打开网页并显示窗口
.\TrayChrome.exe --url "https://www.baidu.com" --open

```

## 🎮 界面功能详解

### 顶部工具栏

| 元素 | 功能 | 操作方式 |
|------|------|----------|
| **☰ 汉堡菜单** | 窗口拖拽和调整 | 左键拖拽移动窗口<br/>右键拖拽调整窗口大小 |
| **地址栏** | 网址输入和显示 | 输入网址后按 Enter 导航 |
| **× 关闭按钮** | 隐藏窗口到托盘 | 左键点击 |

### 底部功能按钮栏

| 按钮 | 图标 | 功能 | 说明 |
|------|------|------|------|
| **后退** | ‹ | 返回上一页 | 浏览历史后退 |
| **前进** | › | 前往下一页 | 浏览历史前进 |
| **刷新** | ⟳ | 重新加载页面 | 刷新当前网页 |
| **收藏夹** | ★ | 收藏夹管理 | 点击显示收藏夹菜单 |
| **暗色模式** | ☾/☼ | 切换网页暗色模式 | 在暗色和亮色模式间切换 |
| **新标签页** | ⫘ | 在默认浏览器打开 | 在系统默认浏览器中打开当前页面 |
| **用户代理** | ▯ | 切换 UA | 在移动端和桌面端 User-Agent 间切换 |
| **置顶** | 📌/⚲ | 窗口置顶 | 切换窗口是否始终保持在最前面 |
| **缩小** | - | 缩小页面 | 减小网页缩放比例 |
| **放大** | + | 放大页面 | 增大网页缩放比例 |
| **拖拽** |  | 拖拽移动 | 拖拽移动窗口位置 |
| **调整** |  | 调整大小 | 拖拽调整窗口大小 |

### 收藏夹功能

- **添加收藏**：点击收藏夹按钮 → "添加到收藏夹"
- **管理收藏**：点击 "open bookmark.json" 编辑收藏夹json文件(删除收藏)
- **快速访问**：收藏夹菜单中直接点击网站名称


## 📁 文件存储

new： 应用程序数据存储在traychrome.exe同目录下

old： 应用程序数据存储在：
```
%APPDATA%\TrayChrome\
├── bookmarks.json    # 收藏夹数据
└── settings.json     # 应用设置
```



## 🛠️ 技术栈

- **框架**：.NET 6.0 + WPF
- **浏览器引擎**：Microsoft WebView2
- **托盘支持**：Hardcodet.NotifyIcon.Wpf
- **目标平台**：Windows 10/11

