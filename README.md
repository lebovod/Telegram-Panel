# Telegram Panel - 多账户管理系统

> 基于 WTelegramClient 的 Telegram 多账户管理面板，支持批量账号管理、频道统计与操作。

## 项目概述

### 核心功能

- **账号管理**
  - 批量上传 session 协议号
  - 手机号 + 密码 + 验证码直接登录
  - 账号分类管理
  - 账号状态监控（在线/封禁/受限）

- **频道管理**
  - 统计账号**创建的**频道（非加入的）
  - 按账号筛选频道
  - 按公开/私密筛选
  - 频道分组保存
  - 批量邀请用户/Bot
  - 批量设置管理员

- **群组管理**
  - 统计账号创建的群组
  - 群组分类与筛选

- **批量操作**
  - 一键创建频道
  - 设置频道公开/私密
  - 批量邀请成员
  - 批量设置管理员权限

## 技术架构

```
┌─────────────────────────────────────────────────────────────┐
│                    前端 (Blazor Server)                      │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐       │
│  │ 账号管理  │ │ 频道管理  │ │ 群组管理  │ │ 任务中心  │       │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘       │
└────────────────────────┬────────────────────────────────────┘
                         │ SignalR (实时通信)
┌────────────────────────┴────────────────────────────────────┐
│                   ASP.NET Core 8.0                           │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │ AccountSvc  │ │ ChannelSvc  │ │  TaskSvc    │           │
│  └─────────────┘ └─────────────┘ └─────────────┘           │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────┴────────────────────────────────────┐
│              WTelegramClient 多实例管理                       │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  ClientPool: Dictionary<int, Client>                │    │
│  │  ┌────────┐ ┌────────┐ ┌────────┐                  │    │
│  │  │Client1 │ │Client2 │ │Client3 │ ...              │    │
│  │  └────────┘ └────────┘ └────────┘                  │    │
│  └─────────────────────────────────────────────────────┘    │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────┴────────────────────────────────────┐
│  SQLite/PostgreSQL  │  Redis (可选)  │  Hangfire (后台任务)  │
└─────────────────────────────────────────────────────────────┘
```

## 技术栈

| 组件 | 技术选型 | 版本 |
|------|---------|------|
| 运行时 | .NET | 8.0 |
| Web框架 | ASP.NET Core | 8.0 |
| 前端 | Blazor Server | 8.0 |
| UI组件 | MudBlazor | 7.x |
| Telegram库 | WTelegramClient | 4.x |
| ORM | Entity Framework Core | 8.0 |
| 数据库 | SQLite (开发) / PostgreSQL (生产) | - |
| 后台任务 | Hangfire | 1.8.x |
| 日志 | Serilog | 3.x |

## 项目结构

```
TelegramPanel/
├── src/
│   ├── TelegramPanel.Web/              # Web应用 (Blazor Server)
│   │   ├── Components/                  # Blazor组件
│   │   │   ├── Layout/                  # 布局组件
│   │   │   ├── Pages/                   # 页面组件
│   │   │   │   ├── Accounts/            # 账号管理页面
│   │   │   │   ├── Channels/            # 频道管理页面
│   │   │   │   ├── Groups/              # 群组管理页面
│   │   │   │   └── Tasks/               # 任务中心页面
│   │   │   └── Shared/                  # 共享组件
│   │   ├── wwwroot/                     # 静态资源
│   │   └── Program.cs
│   │
│   ├── TelegramPanel.Core/             # 核心业务逻辑
│   │   ├── Services/
│   │   │   ├── Telegram/
│   │   │   │   ├── TelegramClientPool.cs       # 客户端池管理
│   │   │   │   ├── AccountService.cs           # 账号服务
│   │   │   │   ├── ChannelService.cs           # 频道服务
│   │   │   │   ├── GroupService.cs             # 群组服务
│   │   │   │   └── SessionImporter.cs          # Session导入
│   │   │   └── Tasks/
│   │   │       ├── BatchInviteTask.cs          # 批量邀请任务
│   │   │       └── SyncDataTask.cs             # 数据同步任务
│   │   ├── Models/
│   │   │   ├── AccountInfo.cs
│   │   │   ├── ChannelInfo.cs
│   │   │   ├── GroupInfo.cs
│   │   │   └── TaskResult.cs
│   │   └── Interfaces/
│   │       ├── ITelegramClientPool.cs
│   │       ├── IAccountService.cs
│   │       └── IChannelService.cs
│   │
│   └── TelegramPanel.Data/             # 数据访问层
│       ├── Entities/
│       │   ├── Account.cs
│       │   ├── AccountCategory.cs
│       │   ├── Channel.cs
│       │   ├── ChannelGroup.cs
│       │   ├── Group.cs
│       │   └── BatchTask.cs
│       ├── Repositories/
│       ├── Configurations/              # EF Core配置
│       └── AppDbContext.cs
│
├── tests/
│   ├── TelegramPanel.Core.Tests/
│   └── TelegramPanel.Web.Tests/
│
├── docs/                                # 文档
│   ├── API.md
│   └── DEPLOYMENT.md
│
├── sessions/                            # Session文件存储 (gitignore)
├── docker-compose.yml
├── TelegramPanel.sln
└── README.md
```

## 数据库设计

### 核心表结构

```sql
-- 账号分类
CREATE TABLE AccountCategories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name NVARCHAR(100) NOT NULL,
    Color NVARCHAR(20),
    Description NVARCHAR(500),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 账号表
CREATE TABLE Accounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Phone NVARCHAR(20) UNIQUE,
    TelegramUserId BIGINT,
    Username NVARCHAR(100),
    FirstName NVARCHAR(100),
    LastName NVARCHAR(100),
    SessionPath NVARCHAR(500),           -- Session文件路径
    ApiId INTEGER NOT NULL,
    ApiHash NVARCHAR(64) NOT NULL,
    CategoryId INTEGER REFERENCES AccountCategories(Id),
    Status NVARCHAR(20) DEFAULT 'active', -- active/banned/limited/offline
    ProxyConfig NVARCHAR(500),           -- 代理配置JSON
    LastSyncAt DATETIME,
    LastActiveAt DATETIME,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 频道分组
CREATE TABLE ChannelGroups (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500),
    Color NVARCHAR(20),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 频道表 (只存储账号创建的频道)
CREATE TABLE Channels (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TelegramId BIGINT UNIQUE NOT NULL,
    AccessHash BIGINT,
    Title NVARCHAR(255),
    Username NVARCHAR(100),              -- NULL表示私密频道
    IsPublic BIT DEFAULT 0,
    IsBroadcast BIT DEFAULT 1,           -- 1=频道, 0=超级群组
    CreatorAccountId INTEGER REFERENCES Accounts(Id),
    GroupId INTEGER REFERENCES ChannelGroups(Id),
    MemberCount INTEGER DEFAULT 0,
    About NVARCHAR(1000),
    TelegramCreatedAt DATETIME,
    SyncedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 群组表 (只存储账号创建的群组)
CREATE TABLE Groups (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TelegramId BIGINT UNIQUE NOT NULL,
    AccessHash BIGINT,
    Title NVARCHAR(255),
    Username NVARCHAR(100),
    CreatorAccountId INTEGER REFERENCES Accounts(Id),
    MemberCount INTEGER DEFAULT 0,
    SyncedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 批量任务表
CREATE TABLE BatchTasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Type NVARCHAR(50) NOT NULL,          -- invite_users/set_admins/create_channel/sync_data
    Name NVARCHAR(200),
    Payload TEXT,                         -- JSON格式的任务参数
    Status NVARCHAR(20) DEFAULT 'pending', -- pending/running/completed/failed/cancelled
    Progress INTEGER DEFAULT 0,
    Total INTEGER DEFAULT 0,
    ResultSummary TEXT,                   -- JSON格式的结果摘要
    AccountId INTEGER REFERENCES Accounts(Id),
    ErrorMessage NVARCHAR(2000),
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    StartedAt DATETIME,
    CompletedAt DATETIME
);

-- 任务日志表
CREATE TABLE TaskLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId INTEGER REFERENCES BatchTasks(Id),
    Level NVARCHAR(20),                   -- info/warning/error
    Message NVARCHAR(2000),
    Details TEXT,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

## 核心API设计

### 账号管理

| 方法 | 路径 | 描述 |
|------|------|------|
| GET | /api/accounts | 获取账号列表 |
| GET | /api/accounts/{id} | 获取账号详情 |
| POST | /api/accounts/login | 手机号登录（发送验证码） |
| POST | /api/accounts/verify | 提交验证码 |
| POST | /api/accounts/import | 批量导入Session |
| DELETE | /api/accounts/{id} | 删除账号 |
| PUT | /api/accounts/{id}/category | 修改账号分类 |
| POST | /api/accounts/{id}/sync | 同步账号数据 |

### 频道管理

| 方法 | 路径 | 描述 |
|------|------|------|
| GET | /api/channels | 获取频道列表（支持筛选） |
| GET | /api/channels/{id} | 获取频道详情 |
| POST | /api/channels | 创建新频道 |
| PUT | /api/channels/{id}/visibility | 设置公开/私密 |
| POST | /api/channels/{id}/invite | 邀请用户/Bot |
| POST | /api/channels/{id}/admins | 设置管理员 |
| PUT | /api/channels/{id}/group | 设置频道分组 |
| POST | /api/channels/batch-invite | 批量邀请 |

### 任务管理

| 方法 | 路径 | 描述 |
|------|------|------|
| GET | /api/tasks | 获取任务列表 |
| GET | /api/tasks/{id} | 获取任务详情 |
| POST | /api/tasks/{id}/cancel | 取消任务 |
| POST | /api/tasks/{id}/retry | 重试失败任务 |

## 快速开始

### 环境要求

- .NET 8.0 SDK
- Visual Studio 2022 / VS Code / Rider
- SQLite (开发环境) 或 PostgreSQL (生产环境)

### 获取 Telegram API 凭据

1. 访问 https://my.telegram.org/apps
2. 登录您的 Telegram 账号
3. 创建新应用，获取 `api_id` 和 `api_hash`

### 本地运行

```bash
# 克隆项目
git clone https://github.com/xxx/TelegramPanel.git
cd TelegramPanel

# 还原依赖
dotnet restore

# 配置 appsettings.json
# 设置 Telegram:ApiId 和 Telegram:ApiHash

# 运行迁移
dotnet ef database update -p src/TelegramPanel.Data -s src/TelegramPanel.Web

# 启动应用
dotnet run --project src/TelegramPanel.Web
```

访问 https://localhost:5001

### Docker 部署

```bash
docker-compose up -d
```

## 配置说明

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=telegram_panel.db"
  },
  "Telegram": {
    "ApiId": 12345678,
    "ApiHash": "your_api_hash_here",
    "SessionsPath": "sessions",
    "DefaultDelay": 2000,
    "MaxRetries": 3
  },
  "Hangfire": {
    "DashboardPath": "/hangfire"
  },
  "Serilog": {
    "MinimumLevel": "Information"
  }
}
```

## 风控注意事项

| 风险点 | 建议措施 |
|--------|---------|
| 频繁操作 | 每次操作间隔 2-5 秒，使用随机延迟 |
| 批量邀请 | 单次最多 200 人，分批执行 |
| 新账号限制 | 新号前几天不要大量操作 |
| IP 问题 | 建议使用代理池，每个账号绑定固定代理 |

## 开发计划

### Phase 1 - 基础框架 (Week 1)
- [x] 项目结构搭建
- [ ] 数据库设计与迁移
- [ ] WTelegramClient 集成
- [ ] 基础 UI 框架

### Phase 2 - 账号管理 (Week 2)
- [ ] Session 文件导入
- [ ] 手机号验证码登录
- [ ] 账号分类管理
- [ ] 账号状态监控

### Phase 3 - 数据同步 (Week 3)
- [ ] 同步创建的频道
- [ ] 同步创建的群组
- [ ] 频道筛选与分组

### Phase 4 - 批量操作 (Week 4)
- [ ] 创建频道
- [ ] 批量邀请用户
- [ ] 设置管理员
- [ ] 任务队列与进度

### Phase 5 - 优化完善 (Week 5)
- [ ] 错误处理与重试
- [ ] 日志与监控
- [ ] 性能优化
- [ ] 文档完善

## License

MIT License

## 贡献

欢迎提交 Issue 和 Pull Request！
