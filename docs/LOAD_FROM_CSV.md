# Load from CSV / 從 CSV 載入

[English](#english) | [繁體中文](#繁體中文)

## English

**When to use it.** Use **Load from CSV...** when you keep numbers outside the model (a spreadsheet, or the CSV of an
earlier export) and want them in the next export: tuned masses and inertias, joint limits, colors. **You do not need it**
to repeat an export: the exporter already stores its settings in the model, per SolidWorks configuration, and loads
them when it opens. It also cannot bring in meshes: mesh file names are always rewritten on export (see
[Things to know](#things-to-know)).

### What it does

Every assembly export writes a CSV next to the URDF (`<package>/urdf/<name>.csv`), one row per link. **Load from
CSV...** on the exporter's PropertyManager page reads such a file and merges its values into the link tree you are
editing. Links are paired **by name** (case sensitive); the tree itself (which link is the parent of which, the SolidWorks
components, the joint names) always comes from the model, not from the CSV.

Columns (first row is the header):

| Group | Columns |
|---|---|
| Link | `Link Name` (the key), `SW Components`, `Material Name` |
| Mass and inertia | `Center of Mass X..Yaw`, `Mass`, `Moment Ixx..Izz` |
| Visual | `Visual X..Yaw`, `Mesh Filename`, `Color Red..Alpha` |
| Collision | `Collision X..Yaw`, `Collision Mesh Filename` |
| Joint kinematics | `Coordinate System`, `Axis Name`, `Joint Name`, `Joint Type`, `Joint Origin X..Yaw`, `Parent`, `Joint Axis X..Z` |
| Other joint values | `Limit Effort/Velocity/Lower/Upper`, `Calibration rising/falling`, `Dynamics Damping/Friction`, `Safety Soft Upper/Lower`, `Safety K Position/Velocity` |

Values are in meters, kilograms and radians, as in the URDF.

### Step by step

1. Open the assembly and start **Tools > Export as URDF**, then build or check the link tree as usual.
2. Click **Load from CSV...** and choose the CSV file. If some links of the current tree are incomplete you are asked
   whether to continue; the merge still works, but check the result.
3. The **URDF Tree Merge** window opens (its texts are in English in every language).

   ![The URDF Tree Merge window with one unmatched link selected](images/load_from_csv_merge_window.png)

   - **Left, "Preview of Updated Configuration"**: the link tree as it will be after the merge. It updates at once when
     you change anything. Links can be dragged inside this tree to move or reorder them.
   - **Middle**: **Matching Links from CSV** (a link of the CSV has the same name as a link of the tree) and
     **Unmatched Links** (no such link in the tree). Click a link to see its CSV values in the list on the right
     ("Properties loaded from CSV").
   - **Bottom, four groups**: *Mass and Inertia*, *Visual Properties*, *Joint Kinematics*, *Other Joint Values*. Each
     has two buttons: the upper one is labeled with the model's name (use the values of the model), the lower one with
     the CSV file name (use the CSV values). Initially Mass and Inertia, Visual Properties and Other Joint Values use the CSV;
     Joint Kinematics uses the model.
   - **Pair an unmatched link**: select it in *Unmatched Links*, type the name of the tree link in the **Link** box and click
     **Change Name**. The link moves to *Matching Links*. This renames the link only inside the window, the CSV file is not
     changed. A name must not be empty or already used by another CSV link. **Reset** only discards what you typed
     in the box and did not apply.
4. Click **Merge**. If CSV links are still unmatched you are asked to confirm; they are ignored. **Cancel** (after a confirmation) changes nothing.
5. The merged values are in the link tree of the exporter page. Nothing is written to the model until you close the page
   with OK or export.

What each group takes from the CSV, for matched links only:

- *Mass and Inertia*: center of mass pose, mass, inertia.
- *Visual Properties*: visual and collision poses, mesh file names, color and material.
- *Joint Kinematics*: joint origin, coordinate system, axis, joint type.
- *Other Joint Values*: limits, calibration, dynamics, safety controller.

### Things to know

These were checked in SolidWorks 2023 with a two-box assembly.

- **A group is taken completely.** Every cell of the group comes from the CSV, and an empty cell becomes 0 or empty. A CSV
  that has only `Link Name` and `Mass` gives that link an inertia of 0 if *Mass and Inertia* uses the CSV. Keep whole rows, or
  choose the model for that group.
- **The choice applies to every link, not only the matched ones.** For a group that uses the CSV, the exporter no longer
  computes that group for any link. A link that is not in the CSV keeps what the tree holds (the values of its last export),
  and a link that was never exported gets 0, for example a mass of 0. This also happens when a CSV from another
  SolidWorks configuration lacks a link of this one, or when nothing matches at all. For a CSV that does not cover all links,
  choose the model's values for the groups where that matters.
- **Mesh file names from the CSV are not kept.** On every export, also **Export URDF Only**, the mesh file names are set to
  `package://<package>/meshes/<link>.STL`. Use the CSV for numbers, colors and joint values.
- **Number format.** The loader reads a comma-separated file with `.` as decimal point, the format the exporter writes.
  If a spreadsheet saved it with a decimal comma or with `;` as separator, nothing is reported: the numbers become 0, or
  the link names are empty and nothing matches. A text in a number cell also becomes 0 without a message. Select a link
  and check the values in the properties list before you merge.
- A row with fewer cells than the header is accepted, the missing cells are empty. The `Parent` column is informational,
  the tree is not rebuilt from it.
- After a merge the exporter page no longer computes the groups you took from the CSV (its "compute" checkboxes for them
  are cleared); tick one again if you want that group recomputed from the model, which overwrites the CSV values.

### Common problems

- *Nothing is matched*: the link names differ. Pair them in the merge window or edit the names in the CSV.
- *All values are 0 after the merge*: the CSV was saved with a decimal comma or `;`, or the group was taken from a CSV
  that does not match. Check the properties list, and see above.
- *A link has mass 0 after the export*: it is not in the CSV and was never exported before. Choose the model's values for
  *Mass and Inertia*.

## 繁體中文

**什麼時候用。** 當你在模型之外維護數值（試算表，或先前匯出的 CSV），想讓下一次匯出使用它們（調好的質量與慣量、
關節限制、顏色）時，使用 **從 CSV 載入…**。**只是重複匯出時不需要**：匯出器已依 SolidWorks 組態把設定存在模型內，
開啟時自動載入。它也無法帶入 mesh：匯出時 mesh 檔名一律重新產生（見[注意事項](#注意事項)）。

### 作用

每次匯出組合件，會在 URDF 旁寫出一個 CSV（`<套件>/urdf/<名稱>.csv`），每個連桿一列。匯出器 PropertyManager 頁面的
**從 CSV 載入…** 會讀取這種檔案，把其中的值合併到目前編輯的連桿樹。連桿**依名稱**（區分大小寫）配對；樹本身
（誰是誰的父連桿、SolidWorks 元件、關節名稱）一律來自模型，不來自 CSV。

欄位（第一列為標題列）：

| 分組 | 欄位 |
|---|---|
| 連桿 | `Link Name`（比對用的鍵）、`SW Components`、`Material Name` |
| 質量與慣量 | `Center of Mass X..Yaw`、`Mass`、`Moment Ixx..Izz` |
| Visual | `Visual X..Yaw`、`Mesh Filename`、`Color Red..Alpha` |
| Collision | `Collision X..Yaw`、`Collision Mesh Filename` |
| 關節運動學 | `Coordinate System`、`Axis Name`、`Joint Name`、`Joint Type`、`Joint Origin X..Yaw`、`Parent`、`Joint Axis X..Z` |
| 其他關節值 | `Limit Effort/Velocity/Lower/Upper`、`Calibration rising/falling`、`Dynamics Damping/Friction`、`Safety Soft Upper/Lower`、`Safety K Position/Velocity` |

數值與 URDF 相同：公尺、公斤、弧度。

### 逐步操作

1. 開啟組合件，執行 **工具 > 匯出為 URDF**，照常建立或檢查連桿樹。
2. 按 **從 CSV 載入…**，選擇 CSV 檔。若目前連桿樹有未填完的連桿，會詢問是否繼續；仍可合併，但請檢查結果。
3. 出現 **URDF Tree Merge** 視窗（視窗文字在所有語言下都是英文）。

   ![URDF Tree Merge 視窗，已選取一個未配對連桿](images/load_from_csv_merge_window.png)

   - **左側「Preview of Updated Configuration」**：合併後的連桿樹預覽，任何改動都會立即更新。可在此樹內拖曳連桿來
     移動或重新排序。
   - **中間**：**Matching Links from CSV**（CSV 中有與樹同名的連桿）與 **Unmatched Links**（樹中沒有同名連桿）。
     點選連桿，右側清單（「Properties loaded from CSV」）會顯示它在 CSV 中的值。
   - **下方四個分組**：*Mass and Inertia*、*Visual Properties*、*Joint Kinematics*、*Other Joint Values*。每組有兩個
     按鈕：上面的以模型名稱為標籤（使用模型的值），下面的以 CSV 檔名為標籤（使用 CSV 的值）。預設
     Mass and Inertia、Visual Properties、Other Joint Values 使用 CSV；Joint Kinematics 使用模型。
   - **配對未配對的連桿**：在 *Unmatched Links* 選取它，在 **Link** 欄輸入樹中連桿的名稱，按 **Change Name**，該連桿
     會移到 *Matching Links*。這只會在視窗內改名，CSV 檔不變。名稱不可空白，也不可與其他 CSV 連桿重複。
     **Reset** 只會捨棄欄位中輸入但尚未套用的文字。
4. 按 **Merge**。若仍有未配對的 CSV 連桿，會要求確認，這些連桿會被忽略。**Cancel**（確認後）不會改變任何東西。
5. 合併後的值出現在匯出頁面的連桿樹。在你按 OK 關閉頁面或匯出之前，不會寫入模型。

各分組對已配對連桿取自 CSV 的內容：

- *Mass and Inertia*：質心位姿、質量、慣量。
- *Visual Properties*：visual 與 collision 位姿、mesh 檔名、顏色與材質。
- *Joint Kinematics*：關節原點、座標系、軸、關節類型。
- *Other Joint Values*：限制、校準、動態、安全控制器。

### 注意事項

以下已在 SolidWorks 2023 以兩個方塊的組合件實測。

- **整個分組一併採用。** 該分組的每個儲存格都取自 CSV，空白儲存格會變成 0 或空值。若 *Mass and Inertia* 用 CSV，而
  CSV 只有 `Link Name` 與 `Mass`，該連桿的慣量會變成 0。請保留完整的列，或讓該分組使用模型的值。
- **選擇對所有連桿生效，不只是已配對的。** 某分組使用 CSV 時，匯出器不再為任何連桿計算該分組。不在 CSV 中的連桿保留
  樹中的值（上次匯出的值），從未匯出過的連桿則為 0，例如質量 0。當另一個 SolidWorks 組態的 CSV 缺少本組態的某個連桿，
  或完全沒有配對時，也會如此。CSV 沒有涵蓋所有連桿時，請在需要的分組選用模型的值。
- **CSV 中的 mesh 檔名不會保留。** 每次匯出（包括 **只匯出 URDF**）都會把 mesh 檔名設為
  `package://<套件>/meshes/<連桿>.STL`。CSV 請用於數值、顏色與關節值。
- **數字格式。** 載入器讀取以逗號分隔、以 `.` 為小數點的檔案，也就是匯出器寫出的格式。若試算表存成小數逗號或以 `;`
  分隔，不會有任何提示：數字變成 0，或連桿名稱為空而完全沒有配對。數字儲存格中的文字同樣會無訊息地變成 0。
  合併前請選取連桿，檢查右側清單的值。
- 儲存格數少於標題列的列可以接受，缺少的儲存格視為空白。`Parent` 欄僅供參考，不會據以重建樹。
- 合併後，匯出頁面不再計算你選用 CSV 的分組（對應的「計算」核取方塊會被取消勾選）；若要讓該分組重新由模型計算，
  請再勾選，這會覆蓋 CSV 的值。

### 常見問題

- *完全沒有配對*：連桿名稱不同。在合併視窗內配對，或直接改 CSV 內的名稱。
- *合併後所有值都是 0*：CSV 存成小數逗號或 `;`，或該分組取自不相符的 CSV。請檢查右側清單，並參考上文。
- *匯出後某連桿質量為 0*：它不在 CSV 中，且先前從未匯出。請讓 *Mass and Inertia* 使用模型的值。
