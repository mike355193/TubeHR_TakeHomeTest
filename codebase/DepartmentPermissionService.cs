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
            string category = null;
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
