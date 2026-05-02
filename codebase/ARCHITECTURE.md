# Foundation 模組架構概覽

## 技術棧
- .NET Framework 4.7.1（部分新服務用 .NET 5）
- EF Core + ADO.NET/Dapper + Stored Procedures（混用）
- SQL Server on Azure + Elastic Scale Sharding（一租戶一 DB）

## 員工生命週期

```
到職(A01) → 在職 → 晉升(A05) / 調部(A07) / ...
                ↘ 留停(A13) → 復職(A02) → 在職
                ↘ 離職(A14) → 再雇用承認年資(A03) → 在職
                             → 再雇用不承認年資(A04) → 在職
```

## 多租戶
- 每個查詢都必須帶 `CompanyId`
- DB 層有 sharding，應用層有 WHERE 過濾（雙保險）

## 跨系統同步
- Foundation 異動後 → 同步到考勤(PT)和薪資(PY)
- 透過 Interface Transfer 機制（非即時 HTTP）
- 失敗記錄到 trans_error_log，管理員手動處理

## 權限架構
- 4 種角色 category：Employee、Secretary、Manager、Hr
- FunctionCode 對應到功能頁面（如 LeaveRecordManagement）
- 資料權限透過 Authorization_FunctionData 控制可見部門範圍

## 檔案說明

| 檔案 | 說明 |
|------|------|
| `Employee.cs` | 員工 Entity Model |
| `EmployeeService.cs` | 核心服務：搜尋、匯入、狀態異動、年資 |
| `RehireService.cs` | 再雇用服務：離職員工回任流程 |
| `DepartmentPermissionService.cs` | 部門權限：各角色可見的部門和員工 |
| `PR-pending-convert-fulltime.cs` | Kevin 的 PR #87（等待 Review） |
