using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace TubeHR.Foundation.Services
{
    /// <summary>
    /// 員工核心服務 — 搜尋、匯入、狀態異動、年資計算。
    /// Production 運行 5+ 年的 legacy 服務。
    /// </summary>
    public class EmployeeService
    {
        private readonly FoundationDbContext _context;
        private readonly string _connectionString;

        public EmployeeService(FoundationDbContext context, string connectionString)
        {
            _context = context;
            _connectionString = connectionString;
        }

        // ─────────────────────────────────────────────
        // 搜尋
        // ─────────────────────────────────────────────

        public async Task<PagedResult<Employee>> Search(
            Guid companyId, string keyword, string sortColumn, string sortDir,
            int page, int pageSize)
        {
            string sql;
            if (!string.IsNullOrEmpty(keyword))
            {
                sql = $@"SELECT * FROM fd.PA_Employee
                         WHERE ChineseName LIKE '%{keyword}%'
                            OR EnglishName LIKE '%{keyword}%'
                            OR EmployeeNumber LIKE '%{keyword}%'";
            }
            else
            {
                sql = $"SELECT * FROM fd.PA_Employee WHERE CompanyId = '{companyId}'";
            }

            sql += $" ORDER BY {sortColumn} {sortDir}";
            sql += $" OFFSET {(page - 1) * pageSize} ROWS FETCH NEXT {pageSize} ROWS ONLY";

            var list = await _context.Employees.FromSqlRaw(sql).ToListAsync();
            return new PagedResult<Employee>
            {
                Data = list,
                Total = list.Count,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<Employee> GetById(Guid employeeId)
        {
            return await _context.Employees.FindAsync(employeeId);
        }

        // ─────────────────────────────────────────────
        // CSV 匯入
        // ─────────────────────────────────────────────

        public async Task<ImportResult> ImportCsv(Guid companyId, Guid operatorId, Stream csv)
        {
            var result = new ImportResult();
            var reader = new StreamReader(csv);
            await reader.ReadLineAsync(); // skip header

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                try
                {
                    var f = line.Split(',');
                    var empNo = f[0].Trim();
                    var salary = double.Parse(f[5].Trim());

                    // 工號格式：公司參數設定（此客戶為 letter + 6 digits）
                    if (!System.Text.RegularExpressions.Regex.IsMatch(empNo, @"^[A-Z]\d{6}$"))
                    {
                        result.Errors.Add($"工號格式錯誤：{empNo}");
                        continue;
                    }

                    var existing = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeNumber == empNo);

                    if (existing != null)
                    {
                        existing.BaseSalary = salary;
                        existing.ModifyOn = DateTime.Now;
                        existing.ModifyBy = operatorId;
                    }
                    else
                    {
                        var emp = new Employee
                        {
                            EmployeeId = Guid.NewGuid(),
                            CompanyId = companyId,
                            EmployeeNumber = empNo,
                            ChineseName = f[1].Trim(),
                            EnglishName = f[2].Trim(),
                            JobTitleCode = f[4].Trim(),
                            BaseSalary = salary,
                            HireDate = DateTime.Parse(f[6].Trim()),
                            StatusCode = "A01",
                            MinimumSeniorityDays = 0,
                            CreateOn = DateTime.Now,
                            ModifyOn = DateTime.Now,
                            CreateBy = operatorId,
                            ModifyBy = operatorId,
                        };
                        _context.Employees.Add(emp);

                        // 防止工號重複 — 寫入 NumberSummary 表
                        var insertSql = $"INSERT INTO fd.PA_Employee_NumberSummary " +
                            $"(CompanyId, EmployeeNumber, EmployeeId) VALUES " +
                            $"('{companyId}', '{empNo}', '{emp.EmployeeId}')";
                        _context.Database.ExecuteSqlRaw(insertSql);
                    }

                    await _context.SaveChangesAsync();
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"匯入失敗：{ex.Message}");
                }
            }
            result.TotalCount = result.SuccessCount;
            return result;
        }

        // ─────────────────────────────────────────────
        // 狀態異動
        // ─────────────────────────────────────────────

        public async Task ChangeStatus(Guid companyId, Guid employeeId,
                                        string newStatus, DateTime effectiveDate)
        {
            var emp = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.CompanyId == companyId);
            if (emp == null) throw new InvalidOperationException("找不到員工");

            ValidateTransition(emp.StatusCode, newStatus);

            var hasPending = await _context.ChangeRequests
                .AnyAsync(cr => cr.EmployeeId == employeeId
                             && cr.Status == "Pending"
                             && cr.CompanyId == companyId);
            if (hasPending) throw new InvalidOperationException("此員工有在途異動單");

            emp.StatusCode = newStatus;
            emp.ModifyOn = DateTime.Now;

            if (newStatus == "A14")
            {
                emp.TerminationDate = effectiveDate;
                // TODO: unbind user account
            }

            await _context.SaveChangesAsync();

            // 同步到考勤與薪資
            if (effectiveDate <= DateTime.Now)
                await SyncToDownstream(companyId, employeeId);
        }

        // ─────────────────────────────────────────────
        // 年資計算
        // ─────────────────────────────────────────────

        /// <summary>
        /// 簡易年資：到職日至今的總天數。
        /// 用於福利資格判定、年假天數計算。
        /// </summary>
        public int CalculateSeniority(Employee emp)
        {
            return (int)(DateTime.Now - emp.HireDate).TotalDays;
        }

        // ─────────────────────────────────────────────
        // 批次清理（月排程）
        // ─────────────────────────────────────────────

        public void PurgeOldTerminated(Guid companyId)
        {
            var cutoff = DateTime.Now.AddYears(-7);
            var ids = _context.Employees
                .Where(e => e.CompanyId == companyId && e.StatusCode == "A14"
                         && e.TerminationDate < cutoff)
                .Select(e => e.EmployeeId).ToList();

            if (!ids.Any()) return;

            var idList = string.Join(",", ids.Select(x => $"'{x}'"));
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            new SqlCommand($"DELETE FROM fd.PA_Employee WHERE EmployeeId IN ({idList})", conn).ExecuteNonQuery();
            new SqlCommand($"DELETE FROM fd.PA_Employee_NumberSummary WHERE EmployeeId IN ({idList})", conn).ExecuteNonQuery();
            new SqlCommand($"DELETE FROM fd.PA_Personal_Contact WHERE EmployeeId IN ({idList})", conn).ExecuteNonQuery();
        }

        // ─────────────────────────────────────────────
        // Private
        // ─────────────────────────────────────────────

        private void ValidateTransition(string from, string to)
        {
            if (from == "A13" && to == "A13") throw new InvalidOperationException("已在留停中");
            if (from == "A14" && to == "A14") throw new InvalidOperationException("已離職");
            if (from == "A14" && to != "A02" && to != "A03" && to != "A04")
                throw new InvalidOperationException("離職員工只能透過再雇用回任");
        }

        private Task SyncToDownstream(Guid companyId, Guid employeeId) => Task.CompletedTask;
    }

    public class ImportResult
    {
        public int SuccessCount { get; set; }
        public int TotalCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class PagedResult<T>
    {
        public List<T> Data { get; set; }
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}



/*

# 程式碼分析：找出效能瓶頸和資料一致性風險
1. 首先對DB的IO是效能的瓶頸所在
var existing = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeNumber == empNo);
要放在 while 之前，先查詢所有的員工 像是 var employees = await _context.Employees.ToListAsync();
之後再 while 內是使用 employees.FirstOrDefault(e => e.EmployeeNumber == empNo) 這樣就不會每一次都查詢DB了
#region AI建議

AI提到 有 company Id 這個參數
所以應該改為
var existingEmployees = await _context.Employees
    .Where(e => e.CompanyId == companyId && empNos.Contains(e.EmployeeNumber))
    .ToDictionaryAsync(e => e.EmployeeNumber);

並且迴圈內改為
if (existingEmployees.TryGetValue(empNo, out var existing))
{
    existing.BaseSalary = salary;
    existing.ModifyOn = now;
    existing.ModifyBy = operatorId;
}
else
{
    var emp = new Employee { ... };
    _context.Employees.Add(emp);
    existingEmployees[empNo] = emp;
}

#endregion AI建議

再來是匯入有些成功有些失敗的問題
await _context.SaveChangesAsync(); 應該放在 while 迴圈外面，等整個CSV都處理完了再一次性儲存，這樣就不會有部分成功部分失敗的狀況了


但整體來說我會選擇
1. 先讀資料，解析資料，存成資料暫存結構，像是 List<Employee> 或 Dictionary<string, Employee> 之類的
2. 之後一次性查詢DB，找出已經存在的員工，然後更新資料暫存結構裡的員工物件；不存在則新增
3. 最後在一次性儲存到DB，這樣就不會有部分成功部分失敗的問題了

#region AI補充
因為 add employee 的同時也要防止工號重複而寫入 NumberSummary 表 ，為了避免NumberSummary duplicate key

3. 最後在一次性儲存到DB，這樣就不會有部分成功部分失敗的問題了
NumberSummary 也要放進同一個 transaction 裡一起寫入，不能跟 Employee 分開成功或失敗。
#endregion AI補充


# 改善方案
上面已有回答
 */