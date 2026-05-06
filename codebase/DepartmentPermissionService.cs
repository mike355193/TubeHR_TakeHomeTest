using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace TubeHR.Foundation.Services
{
    /// <summary>
    /// 部門權限查詢服務 — 控制各角色能看到哪些部門和員工。
    /// 這是考勤和薪資模組的共用服務，前端篩選器的資料來源。
    /// </summary>
    public class DepartmentPermissionService
    {
        private readonly string _connectionString;

        public DepartmentPermissionService(string connectionString)
            => _connectionString = connectionString;

        /// <summary>
        /// 取得使用者可見的部門清單。
        /// 由考勤模組的請假/加班/出差頁面呼叫。
        /// </summary>
        public async Task<List<DepartmentDto>> GetDepartments(
            Guid companyId, Guid employeeId, Guid userId, string functionCode)
        {
            // 查詢此 functionCode 的角色 category
            string category = null; //請考慮宣告為 string.empty 或者宣告為 string? 型別
            if (!string.IsNullOrWhiteSpace(functionCode))
            {
                category = await FindCategory(companyId, userId, functionCode);

                // 非 HR 角色不做資料權限過濾
                if (category != "Hr")
                    functionCode = null;
            }

            // 查詢員工（含部門資訊），再 group by 部門
            var employees = await SearchEmployees(companyId, employeeId, functionCode);

            return employees
                .Where(e => e.DepartmentId.HasValue)
                .GroupBy(e => new { e.DepartmentId, e.DeptCode, e.DeptName })
                .OrderBy(g => g.Key.DeptCode)
                .Select(g => new DepartmentDto
                {
                    DepartmentId = g.Key.DepartmentId.Value,
                    DeptCode = g.Key.DeptCode,
                    DeptName = g.Key.DeptName,
                    Employees = g.Select(e => new EmployeeDto
                    {
                        EmployeeId = e.EmployeeId,
                        EmployeeNumber = e.EmployeeNumber,
                        ChineseName = e.ChineseName,
                        Status = e.Status
                    }).ToList()
                }).ToList();
        }

        /// <summary>
        /// 員工搜尋 — 如果 functionCode 不為 null，會套用 FunctionDataFilter 做權限過濾。
        /// 如果 functionCode 為 null，回傳全部。
        /// </summary>
        private async Task<List<EmployeeRecord>> SearchEmployees(
            Guid companyId, Guid employeeId, string functionCode)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT e.EmployeeId, e.EmployeeNumber, e.ChineseName, e.Status,
                       e.DepartmentId, d.DeptCode, d.DeptName
                FROM fd.I_Employee e
                LEFT JOIN fd.I_Department d ON e.DepartmentId = d.DepartmentId
                WHERE e.CompanyId = @CompanyId";

            if (functionCode != null)
            {
                // 套用 FunctionDataFilter（依角色的資料權限過濾部門範圍）
                sql += @" AND e.DepartmentId IN (
                    SELECT DepartmentId FROM ba.Authorization_FunctionData
                    WHERE FunctionCode = @FunctionCode AND EmployeeId = @EmployeeId
                )";
            }

            sql += " ORDER BY e.EmployeeNumber";

            return (await conn.QueryAsync<EmployeeRecord>(sql, new
            {
                CompanyId = companyId,
                FunctionCode = functionCode,
                EmployeeId = employeeId
            })).ToList();
        }

        private async Task<string> FindCategory(Guid companyId, Guid userId, string functionCode)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            return await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT Category FROM ba.Authorization_Function WHERE FunctionCode = @FunctionCode",
                new { FunctionCode = functionCode });
        }

        /// <summary>
        /// BPM 表單查詢權限 — 查詢使用者可見的簽核表單。
        /// 用於「待我簽核」清單。
        /// </summary>
        public async Task<int> GetPendingApprovalCount(Guid companyId, Guid employeeId,
                                                        List<string> permissionIds)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // 建立暫存表存放權限 ID
            var permIdsString = $"('{string.Join("'),('", permissionIds)}')";
            var sql = $@"
                DECLARE @permIds TABLE (PermId NVARCHAR(50))
                INSERT INTO @permIds VALUES {permIdsString}

                SELECT COUNT(*) FROM gbpm.fm_form_approve fa
                INNER JOIN @permIds p ON fa.ApproveEmployeeId = p.PermId
                WHERE fa.Status = 'Pending'";

            return await conn.QueryFirstAsync<int>(sql);
        }
    }

    public class DepartmentDto
    {
        public Guid DepartmentId { get; set; }
        public string DeptCode { get; set; }
        public string DeptName { get; set; }
        public List<EmployeeDto> Employees { get; set; }
    }

    public class EmployeeDto
    {
        public Guid EmployeeId { get; set; }
        public string EmployeeNumber { get; set; }
        public string ChineseName { get; set; }
        public string Status { get; set; }
    }

    internal class EmployeeRecord
    {
        public Guid EmployeeId { get; set; }
        public string EmployeeNumber { get; set; }
        public string ChineseName { get; set; }
        public string Status { get; set; }
        public Guid? DepartmentId { get; set; }
        public string DeptCode { get; set; }
        public string DeptName { get; set; }
    }
}


/*

首先，對於 string xxx = null; 的宣告，建議改為 string? xxx = null;，以明確表示這個變數可以為 null。
或者，如果你希望它永遠不為 null，可以宣告為 string xxx = string.Empty;。

IsNullOrWhiteSpace的判斷也可以改為 IsNullOrEmpty，視需求而定。如果你希望空白字串也被視為無效，那麼 IsNullOrWhiteSpace 是適合的。


--------------------

# 根因分析:
 
if (!string.IsNullOrWhiteSpace(functionCode))
{
    category = await FindCategory(companyId, userId, functionCode);

    // 非 HR 角色不做資料權限過濾
    if (category != "Hr")
        functionCode = null;
}

上段把非 Hr 角色的 functionCode 設為 null，讓下段的 SearchEmployees 不套用 FunctionDataFilter，回傳全部資料。

if (functionCode != null)
{
    // 套用 FunctionDataFilter（依角色的資料權限過濾部門範圍）
    sql += @" AND e.DepartmentId IN (
        SELECT DepartmentId FROM ba.Authorization_FunctionData
        WHERE FunctionCode = @FunctionCode AND EmployeeId = @EmployeeId
    )";
}

functionCode = null 目前等於「查全公司」

所以
if (category != "Hr")
    functionCode = null; 這段不能留著，否則非 HR 角色會看到全部資料。


#region 以下AI建議
category 也要傳進 SearchEmployees
並且建議修改程式碼如下：
(大綱邏輯：根據 category 決定 SQL 的 where 的條件)(照理只應該看到自己部門)

if (category == "Hr")
    {
        sql += @" AND e.DepartmentId IN (
            SELECT DepartmentId FROM ba.Authorization_FunctionData
            WHERE FunctionCode = @FunctionCode
              AND EmployeeId = @EmployeeId
        )";
    }
    else if (category == "Employee")
    {
        sql += @" AND e.DepartmentId = (
            SELECT DepartmentId
            FROM fd.I_Employee
            WHERE CompanyId = @CompanyId
              AND EmployeeId = @EmployeeId
        )";
    }
    else if (category == "Manager")
    {
        sql += @" AND e.DepartmentId IN (
            SELECT DepartmentId
            FROM fd.I_ManagerDepartment
            WHERE CompanyId = @CompanyId
              AND ManagerEmployeeId = @EmployeeId
        )";
    }
    else if (category == "Secretary")
    {
        sql += @" AND e.DepartmentId IN (
            SELECT DepartmentId
            FROM fd.I_SecretaryDepartment
            WHERE CompanyId = @CompanyId
              AND SecretaryEmployeeId = @EmployeeId
        )";
    }
    else
    {
        return new List<EmployeeRecord>();
    }

#endregion 以上AI建議


# 影響範圍:
影響所有角色:非 HR 角色會看到全部資料，應該根據每個角色決定他們自己的權限資料(照理只應該看到自己部門)
影響API:這問題我根據對話紀錄
> 14:00:01 User=alice@acme.com | Role=Employee | GET /api/departments → 342 records
> 14:00:02 User=alice@acme.com | Role=Employee | GET /api/employees/NameFind → 1,247 records
> 14:02:15 User=hr_admin@acme.com | Role=Hr | GET /api/departments → 15 records
> 14:05:30 User=bob_mgr@acme.com | Role=Manager | GET /api/departments → 342 records
> 14:08:00 User=carol_sec@acme.com | Role=Secretary | GET /api/departments → 342 records
判定有問題的API: GET /api/departments (DepartmentPermissionService.GetDepartments) 和 GET /api/employees/NameFind (假設也會套用同樣的權限邏輯)

# 客戶溝通摘要:
非 HR 角色（如 Employee、Manager、Secretary）在取得部門資料時，會看到全部部門和員工資料，而不是根據他們的角色權限過濾後的資料。
目前技術已經排查到問題所在，待下次更新(假設是熱修就熱修、假設是排程更新就排程更新)就可以修復此問題。

# 修復方案 + 時程估計:
已在第一點回答答案，時程大概半天的開發和測試
 */