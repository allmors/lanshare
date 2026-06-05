# LanShare Memory

## 项目定位

- 项目名：`LanShare`
- 技术栈：`.NET 8`、`WPF`、`MVVM`
- 目标系统：
  - `Windows 10 x64`
  - `Windows 11 x64`
  - `Windows Server 2022 x64`
- 交付形态：双程序
  - `LanShare.Client`
  - `LanShare.Server`

## 当前产品方向

- 用于局域网文件共享与传输
- 不使用 `SMB`
- 不使用数据库
- 配置全部走 `JSON`
- 服务端负责权限控制
- 客户端只消费服务端开放出来的能力

## 当前架构

### 客户端

- 项目：`LanShare.Client/LanShare.Client.csproj`
- 面向终端用户，尽量简化操作
- 主要能力：
  - 浏览共享目录
  - 下载文件
  - 下载文件夹
  - 上传文件
  - 上传文件夹
  - 拖拽上传
  - 删除文件/目录
  - 创建文件夹
- 托盘行为：
  - 最小化保留任务栏
  - 关闭隐藏到托盘

### 服务端

- 项目：`LanShare.Server/LanShare.Server.csproj`
- 主要能力：
  - 选择共享目录
  - 启动文件服务
  - 广播服务发现
  - 设置权限规则
  - 查看连接客户端
- 托盘行为：
  - 关闭隐藏到托盘

## 网络与传输

- 发现：`UDP broadcast`
- 文件操作：`HTTP API`
- 实际传输：`HTTP/TCP`
- 当前默认端口：
  - `ServicePort = 49443`
  - `DiscoveryPort = 49450`

## 权限模型

- 用户：
  - `admin`
  - `guest`
- 权限：
  - `Read`
  - `Write`
  - `Delete`
- 粒度：
  - 目录级
  - 支持对子目录继承
- 规则行为：
  - 新增规则是追加
  - 编辑规则只替换当前规则
  - 完全相同的重复规则不应允许继续添加

## 配置文件

- 根模板：`lanshare.json`
- 客户端运行配置：
  - `%AppData%\LanShare.Client\lanshare.client.json`
- 服务端运行配置：
  - `%AppData%\LanShare.Server\lanshare.server.json`

### 客户端重点配置

- `BuiltInServerHost`
- `BuiltInServerPort`
- `PreferredServerAddress`
- `AutoConnectPreferredServerOnStartup`
- `DownloadFolder`

### 服务端重点配置

- `ServerName`
- `SharedFolderPath`
- `ServicePort`
- 权限规则
- 并发保护参数

## 已完成的重要功能

- 客户端/服务端拆分完成
- 客户端支持文件夹递归上传
- 客户端支持文件夹下载
- 服务端目录下载改为 zip 流式打包下载
- 中文目录名下载 `500` 已修复
- 客户端支持重复文件检测
- 客户端支持拖拽上传前确认
- 服务端共享路径预览已改为可导航模式
  - 双击进入目录
  - 返回上级
  - 当前层级搜索过滤
- 服务端已增加并发保护：
  - 限制同时目录下载数
  - 限制同时上传数
  - 同一路径上传串行化
  - 目录下载中阻止同路径上传/删除/建目录

## 当前稳定性策略

- 大文件传输使用流式读写
- 客户端目录下载失败写入：
  - `%AppData%\LanShare.Client\directory-download-client.log`
- 服务端目录下载日志写入：
  - `%AppData%\LanShare.Server\logs\directory-download.log`
- 客户端现在会把 `409 Conflict` 转成用户可理解的提示，而不是直接显示 HTTP 报错

## 打包与发布

- 发布脚本：
  - `scripts/publish-win-x64.ps1`
- 安装包脚本：
  - `scripts/build-installer.ps1`
- 安装器脚本：
  - `installer/LanShare.Client.iss`
  - `installer/LanShare.Server.iss`
- `build-installer.ps1` 已兼容当前机器的 Inno Setup 路径：
  - `C:\Users\Admin\AppData\Local\Programs\Inno Setup 6\ISCC.exe`

## 当前产物

- 客户端安装包：
  - `dist/LanShare-Client-Setup-0.01.exe`
- 服务端安装包：
  - `dist/LanShare-Server-Setup-0.01.exe`

## 协作注意事项

- 用户对“大改、重写、界面乱动”非常敏感
- 优先做小范围、可验证、可回滚的修改
- 每次改动后尽量直接编译验证
- PowerShell 输出中文可能乱码，但不代表源码一定乱码
- 处理中文内容时，优先以编辑器实际文件内容为准
