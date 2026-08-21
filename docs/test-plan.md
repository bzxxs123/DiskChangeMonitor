# 测试计划 (Test Plan)

## 自动化测试

运行方式：

```powershell
dotnet test DiskChangeMonitor.sln -c Release
```

覆盖范围：

| 模块 | 文件 | 覆盖点 |
|---|---|---|
| 领域模型 | `tests/.../Models/ModelTests.cs` | 记录类型往返、路径规范化、`FormatBytes`、类型标签 |
| CSV 解析 | `tests/.../Import/WizTreeCsvParserTests.cs` | UTF-8 BOM、CRLF、带引号中文路径、科学计数法数字、空白目录字段、目录推断、必需列校验、坏行跳过与行号、本地化日期、空行、未闭合引号、10 万行流式与进度 |
| 快照存储 | `tests/.../Storage/SqliteSnapshotStoreTests.cs` | 建库、提交往返、最新优先列表、路径排序流、5 次保留剪枝、取消不污染历史、暂存不可见、指纹更新、10k 行批量写入 |
| 对比引擎 | `tests/.../Diff/DiffEngineTests.cs` | 新增/删除/变大/变小/未变化分类、大小与分配双指标、元数据移动配对、配对上限、确定性排序、空快照、目录聚合 |
| 导入流程 | `tests/.../Import/ImportCoordinatorTests.cs` | 成功导入并对比、首次导入空对比、缺列拒绝且历史不变、坏行警告传播、取消清理暂存、最新两次对比、源文件不被复制/修改、指纹差异 |
| CSV 导出 | `tests/.../Export/CsvExporterTests.cs` | 逗号/引号转义、UTF-8、稳定列、空报告 |
| 视图模型 | `tests/.../ViewModels/MainViewModelTests.cs` | 根/历史加载、导入命令、缺失文件提示、筛选与搜索、导出命令 |
| 端到端 | `tests/.../Integration/SnapshotComparisonTests.cs` | 两次导出保存→重载→对比、7 次导入剪枝到 5、20 万行大文件流式导入 |

## 手工验收清单

1. 用 WizTree 导出 `C:\` 为 CSV（UTF-8，中文版）。
2. 首次导入：状态显示“导入完成”，历史出现 1 条，总览显示“暂无对比结果”。
3. 修改/新增/删除若干文件后再次导出并导入：总览出现逻辑大小与分配空间变化；目录汇总与明细中出现对应项目。
4. 筛选“新增/删除/变大/变小/移动”与路径关键字，确认结果正确。
5. 连续导入 6 次以上，确认历史只保留最近 5 次。
6. 导入一个故意缺列（如缺“分配”）的 CSV：提示“缺少必需列”，历史不变。
7. 导入含坏行（数字/日期格式错误）的 CSV：忽略行数与警告正确显示。
8. 点击“导出结果 CSV…”：文件可用 Excel/WPS 打开，中文路径不乱码。
9. 导入期间点击“开始导入”无反应（忙碌保护），导入完成后按钮恢复。
