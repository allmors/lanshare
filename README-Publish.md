# LanShare 发布说明

## 当前交付形态

- 双程序：
  - `LanShare.Client`
  - `LanShare.Server`
- 平台：
  - `Windows 10 x64`
  - `Windows 11 x64`
  - `Windows Server 2022 x64`
- 发布方式：
  - `self-contained` 自包含
  - 目标机器不需要单独安装 `.NET 8`

## 1. 生成自包含发布目录

在项目根目录执行：

```powershell
.\scripts\publish-win-x64.ps1
```

可选预编译优化：

```powershell
.\scripts\publish-win-x64.ps1 -ReadyToRun
```

输出目录：

```text
publish\client-win-x64
publish\server-win-x64
```

## 2. 生成安装包

在项目根目录执行：

```powershell
.\scripts\build-installer.ps1
```

说明：

- 脚本会自动查找 `ISCC.exe`
- 当前已兼容以下路径：
  - `C:\Users\Admin\AppData\Local\Programs\Inno Setup 6\ISCC.exe`
  - `C:\Program Files\Inno Setup 6\ISCC.exe`
  - `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

输出目录：

```text
dist
```

当前安装包命名：

- `LanShare-Client-Setup-0.01.exe`
- `LanShare-Server-Setup-0.01.exe`

## 3. 配置文件位置

开发模板配置：

- 项目根目录：`lanshare.json`

运行后用户配置：

- 客户端：`%AppData%\LanShare.Client\lanshare.client.json`
- 服务端：`%AppData%\LanShare.Server\lanshare.server.json`

说明：

- 打包后程序优先使用各自 `AppData` 下的配置文件
- 客户端内置地址、端口、下载目录等，改客户端配置
- 服务端监听端口、共享目录、权限规则、并发参数等，改服务端配置

## 4. 传输与发现机制

- 服务发现：`UDP broadcast`
- 文件浏览、上传、下载、删除、建目录：`HTTP API`
- 实际传输链路：`HTTP/TCP`
- 不使用：`SMB`

## 5. 当前重要行为

- 客户端已支持：
  - 文件下载
  - 文件夹下载
  - 文件上传
  - 文件夹递归上传
  - 拖拽上传
  - 重名文件检测
  - 取消当前传输
- 服务端已支持：
  - 目录级权限
  - 子目录继承
  - 上传/目录下载并发保护

## 6. 近期传输相关修复

- 默认下载目录优先为 `D:\LanShare`
  - 如果机器没有 `D:` 盘，则回退到 `%UserProfile%\Downloads\LanShare`
- 大文件上传已改为原始流直传
  - 更适合几十 GB 文件
  - 上传中断时服务端会清理半截文件
- 中文文件名上传已修复
  - 不再通过请求头传文件名
  - 改为 URL 编码参数传文件名
- 客户端目录下载已改为递归逐文件下载
  - 不再依赖服务端 `zip` 实时压缩
  - 服务端 `/api/download-directory` 仍保留作兼容兜底，但当前客户端正常不会调用
- 目录下载进度已优化
  - 当前文件显示单文件进度
  - 整体目录显示总文件数与总体字节进度
- 客户端遇到 `409 Conflict` 时显示友好提示，而不是原始 HTTP 报错

## 7. 发布前检查

- 确认客户端和服务端都能正常启动
- 确认服务端监听端口与客户端目标端口一致
- 确认 Windows 防火墙已放行
- 确认共享目录有访问权限
- 确认大文件上传、文件夹下载、删除、建目录都已实测
- 确认中文文件名上传正常

## 8. 常用命令

编译客户端：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\LanShare.Client\LanShare.Client.csproj -c Release
```

编译服务端：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build .\LanShare.Server\LanShare.Server.csproj -c Release
```

重新打客户端安装包：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish '.\LanShare.Client\LanShare.Client.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o '.\publish\client-win-x64'
.\scripts\build-installer.ps1 -ScriptPaths .\installer\LanShare.Client.iss
```

重新打服务端安装包：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' publish '.\LanShare.Server\LanShare.Server.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o '.\publish\server-win-x64'
.\scripts\build-installer.ps1 -ScriptPaths .\installer\LanShare.Server.iss
```
