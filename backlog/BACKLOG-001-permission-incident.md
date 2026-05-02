# BACKLOG-001：部門權限資料外洩事件

**優先級**：🔴 P0  
**指派**：未指派  
**狀態**：調查中  

## 描述

ACME 客戶回報：Employee 角色看到全公司部門和員工清單。

詳細 log 見 `team-chat/slack-channel.md` 中 Arthur 的訊息。

## 需要的產出

- [ ] 根因分析（為什麼 Employee 看到全部、HR 只看到部分？）
- [ ] 影響範圍（哪些角色、哪些 API、哪些頁面？）
- [ ] 客戶溝通摘要（給 PM 用，非技術語言）
- [ ] 修復方案 + 時程估計

## 相關程式碼

`codebase/DepartmentPermissionService.cs`
