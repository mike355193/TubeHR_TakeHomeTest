# 工作日誌

> 記錄你的思路、決策、取捨。不需要鉅細靡遺，但讓我們看到你怎麼思考。

## 我讀完資料後，最先做的事是什麼？為什麼？

P0的重要程度當然是最高的。雖然我是一名Senior，但也是一名新人尚未了解目前的專案架構與相關商業邏輯的前提下我不會貿然加入排查。
要是幫倒忙可就不好了。
P1的重要程度是次高的，但涉及資料流。且如果用話術協商不要一次匯入三千筆，服務應該還能正常運作。
既然都已經提到如果新來的朋友能夠協助 review P2 的工作，那我會先處理 P2 的工作。

假設P2我處理完了，我會先閱讀跟P0有關的東西，且旁觀他們在討論P0的問題，看看他們是怎麼分析的，從中了解資料流與相關商業邏輯。
又雖然我不瞭解目前專案架構與相關商業邏輯，但說不定我的一些見解能夠突破目前的思路，幫助團隊找到問題的根源。
若不行，我也是能從此次事件了解到商業邏輯與相關專案架構與資料流。

但又除非我完成P2後有確實指派我去處理P1，或者其他任務(甚至是開發)。
那我就完全不會參與P0的事件。


## 我做了哪些取捨？

上題已有回答。

## 哪些地方我用了 AI？AI 有沒有幫倒忙？

在處理P2的過程中，我先自行閱讀
因為P2的處理方法是提供回饋給 Junior，不是要我直接下去改程式碼，所以我先閱讀了 Junior 的 PR，然後寫下了我的 review comments。
最後有使用codex幫我 review，方向跟我的想法大致一致

處理時間大概30分鐘

----------

在處理P0的過程中，我發現沒有我一開始想的這麼複雜。
我有先自行閱讀，有察覺到不合理的地方 (如果不是Hr，functionCode = null)
我直接將cs丟給 codex分析，並且告知問題所在以及規格

並且提問解決方案大綱
影響範圍、客戶溝通摘要、修復方案 + 時程估計是我自己的想法

耗時大概50分鐘

----------

在處理P1得過程中我一樣有先閱讀
一眼就看到迴圈內DB IO的問題
在一邊整理我看到的想法一邊把問題餵給codex
並且把我的想法告知codex
他有補充我忽略掉的細節

耗時大概30分鐘


--------------------



## 如果再給我 3 小時，我接下來會做什麼？

P2的工作僅提供建議，我是不會幫Junior直接改程式碼的。

P0的工作，我實在很想知道為什麼要宣告string xxx = null;會把整份專案調整為 string? xxx = null;是我的..程式潔癖。
FindCategory 的部分，我會想像Category是個不經常變動的資料；我會選擇從Redis快取中讀取，若沒有才從DB讀取，並且把讀取到的資料放到Redis快取中。
GetPendingApprovalCount 的部分，我覺得Approval應該有更多其他的事項例如 create update delete 等等；他應該另外抽出來獨立一個自己一個ApprovalService，專門負責處理跟Approval相關的邏輯。
Permission是Permission；Approval是Approval；兩者不應該混在一起。
SearchEmployees同理、FindCategory同理；Employees有Employees；Category有Category；但我知道這只是為了候選人方便閱讀所以寫在一起。
又Permission命名為DepartmentPermission，感覺會有其他XXXPermission?
假設有，我會選擇把它做成interface 然後把一些基本的 CRUD 放在裡面，然後讓 DepartmentPermission 繼承這個 interface 實作；並且其他的XXXPermission 也繼承這個 interface 實作。

P1的工作，就會發現到有 Search、GetById
也許也會有 Update、Delete 等等；我會選擇把它們抽出來獨立成一個 BaseService

所有跟DB存取的CRUD邏輯都放在BaseService裡面，然後讓其他的Service繼承這個BaseService統一實作；
除非某個Service的部分邏輯比較複雜一點，可以選擇 override BaseService裡面的CRUD邏輯；但如果只是單純的CRUD邏輯，那就直接繼承BaseService裡面的CRUD邏輯就好，不需要再重複寫一次。

關於PurgeOldTerminated這個方法
我有看到 e.StatusCode == "A14" 這段敘述，
建議把 A14 這種東西改為 enum 或者 const string 的變數，這樣比較有可讀性，也比較不容易出錯。
其實回過頭來 P0 的 Hr、Employee、Manager、Secretary 等等，也都該改

ChangeStatus 看似也有資料一致性問題，他在 SaveChange 後才SyncToDownstream
如果這個 SyncToDownstream 跟 P2 的事情是一樣的，那P2應改呼叫此方法
而不是一樣的事情寫兩遍


----- 
全部的事情我是在三小時內完成的
相關筆記都是在寫在相關的cs檔案最下面註解
