# Load Configuration from a CSV file / 從 CSV 載入設定

[English](#english) | [繁體中文](#繁體中文)

## English

### What it is for

Every assembly export writes a CSV file next to the URDF (`<package>/urdf/<name>.csv`). It holds one row per
link: mass properties, visual and collision poses, mesh file names, colors, and the joint to the parent.
**Load from CSV...** in the exporter's PropertyManager page reads such a file and merges its values into
the link tree you are editing. Use it to
- reuse the tuned values of an earlier export (masses, inertias, joint limits, colors) after the CAD model changed,
- copy the setup from one assembly to a similar one,
- edit values in a spreadsheet and bring them back.

It does **not** read the settings stored in the model itself. Those are loaded automatically when the exporter
opens, for the active SolidWorks configuration.

### Where the CSV comes from

Export the assembly once (**Export URDF and Meshes...** or **Export URDF Only...**). The file
`<package>/urdf/<package>.csv` is created with the URDF. Columns (first row is the header):

| Group | Columns |
|---|---|
| Link | `Link Name` (the key), `SW Components`, `Material Name` |
| Mass and inertia | `Center of Mass X..Yaw`, `Mass`, `Moment Ixx..Izz` |
| Visual | `Visual X..Yaw`, `Mesh Filename`, `Color Red..Alpha` |
| Collision | `Collision X..Yaw`, `Collision Mesh Filename` |
| Joint kinematics | `Coordinate System`, `Axis Name`, `Joint Name`, `Joint Type`, `Joint Origin X..Yaw`, `Parent`, `Joint Axis X..Z` |
| Other joint values | `Limit Effort/Velocity/Lower/Upper`, `Calibration rising/falling`, `Dynamics Damping/Friction`, `Safety Soft Upper/Lower`, `Safety K Position/Velocity` |

Values are plain numbers in meters, kilograms and radians, as in the URDF. You can edit them in a spreadsheet; keep
the header row and save as CSV.

### Step by step

1. Open the assembly and start **Tools > Export as URDF**.
2. Build or check the link tree on the left as usual.
3. Click **Load from CSV...** and choose the CSV file.
4. The merge window opens (its heading is **URDF Tree Merge**; the window itself is not translated). Links are matched **by name**: a CSV row whose `Link Name` equals a link
   of the current tree is *matched*, any other row is *unmatched*.
   - The left tree previews the result. The lists show the matched and the unmatched CSV links; click a link to see
     its CSV values.
   - Per property group (Mass and Inertia, Visual Properties, Joint Kinematics, Other Joint Values) choose
     whether to **use the existing values** (the button named after the assembly) or **the loaded values** (the
     button named after the CSV file); the selected button is highlighted. Mass and Inertia, Visual Properties and
     Other Joint Values start on the loaded values, Joint Kinematics starts on the existing values. Joint Kinematics
     covers the coordinate system, axis and joint type; Other Joint Values covers limits, calibration, dynamics and
     safety controller.
   - To pair a CSV link with a link of the tree, select the CSV link, type the tree link's name in the name box and
     click **Change Name**. The link moves to the matched list; the property list on the right keeps showing the
     original CSV name. **Reset** restores the CSV name. A name must not be empty or already used by another CSV link.
5. Click **Merge**. If CSV links are still unmatched you are asked to confirm; they are ignored.
6. The merged values appear in the link tree. The page now shows **Filename: <the CSV file>** and four check
   boxes: **Compute Mass and Inertia**, **Compute Visual and Collision**, **Compute Joint Kinematics** and
   **Compute Joint Limits**. A group that took the loaded values is unchecked, so the export keeps those values;
   check the box to have the exporter compute that group from the model again instead. A group that kept the
   existing values is shown checked and greyed out. Review the result, then export as usual.

SolidWorks components are never taken from the CSV, they always come from the model. Links that exist only in the
tree keep their values.

### Common problems

- *Nothing is matched*: the link names differ. Rename the CSV links in the merge window, or edit the names in the CSV.
- *"This model has not been fully exported and saved" prompt*: the current tree still has links without all
  required fields. You can continue; check the result afterwards.
- *Merging failed*: the CSV is damaged (missing header, text in a number column). Re-export or fix the file.
- Link names are case sensitive and must be unique.

## 繁體中文

### 用途

每次匯出組合件時，會在 URDF 旁寫出一個 CSV 檔（`<套件>/urdf/<名稱>.csv`），每個連桿一列：質量特性、
Visual／Collision 位姿、Mesh 檔名、顏色，以及連到父連桿的關節。匯出器 PropertyManager 頁面的
**從 CSV 載入…** 會讀取這種檔案，把其中的值合併到目前正在編輯的連桿樹。適用情境：
- 模型修改後，沿用先前匯出調好的值（質量、慣量、關節限制、顏色）；
- 把一個組合件的設定複製到相似的組合件；
- 用試算表編輯數值後再帶回來。

它**不會**讀取存在模型內的設定（那些會在開啟匯出器時，依作用中的 SolidWorks 組態自動載入）。

### CSV 從哪裡來

先匯出一次組合件（**匯出 URDF 與網格…** 或 **僅匯出 URDF…**），`<套件>/urdf/<套件>.csv` 會和 URDF 一起產生。
第一列是欄名，欄位分組如下：

| 分組 | 欄位 |
|---|---|
| 連桿 | `Link Name`（比對用的鍵）、`SW Components`、`Material Name` |
| 質量與慣量 | `Center of Mass X..Yaw`、`Mass`、`Moment Ixx..Izz` |
| Visual | `Visual X..Yaw`、`Mesh Filename`、`Color Red..Alpha` |
| Collision | `Collision X..Yaw`、`Collision Mesh Filename` |
| 關節運動學 | `Coordinate System`、`Axis Name`、`Joint Name`、`Joint Type`、`Joint Origin X..Yaw`、`Parent`、`Joint Axis X..Z` |
| 其他關節值 | `Limit Effort/Velocity/Lower/Upper`、`Calibration rising/falling`、`Dynamics Damping/Friction`、`Safety Soft Upper/Lower`、`Safety K Position/Velocity` |

數值與 URDF 相同：公尺、公斤、弧度。可用試算表編輯，保留標題列並存成 CSV。

### 逐步操作

1. 開啟組合件，執行 **工具 > 匯出為 URDF**。
2. 照常在左側建立或檢查連桿樹。
3. 按 **從 CSV 載入…**，選擇 CSV 檔。
4. 出現合併視窗（標題為 **URDF Tree Merge**，視窗本身沒有翻譯）。連桿**依名稱**配對：CSV 中 `Link Name` 與目前連桿樹中某連桿同名者為「已配對」，
   其餘為「未配對」。
   - 左側樹預覽合併結果；清單列出已配對與未配對的 CSV 連桿，點選連桿可看其 CSV 值。
   - 每個屬性群組（質量與慣量、Visual 屬性、關節運動學、其他關節值）可選擇**使用現有值**（以組合件命名的按鈕）或
     **載入值**（以 CSV 檔名命名的按鈕），選中的按鈕會反白。質量與慣量、Visual 屬性、其他關節值預設選載入值，
     關節運動學預設選現有值。關節運動學含座標系、軸與關節類型；其他關節值含限制、校準、動態與安全控制器。
   - 要把某個 CSV 連桿對應到樹中的連桿：選取該 CSV 連桿，在名稱欄輸入樹中連桿的名稱，按 **Change Name**。
     連桿會移到已配對清單，右側的屬性清單仍顯示原本的 CSV 名稱。**Reset** 還原為 CSV 的名稱。名稱不可空白，也不可與其他 CSV 連桿重複。
5. 按 **Merge**。若仍有未配對的 CSV 連桿，會要求確認，這些連桿會被忽略。
6. 合併後的值會出現在連桿樹。頁面會顯示 **檔案名稱：<CSV 檔名>** 與四個核取方塊：**計算質量與慣性**、
   **計算外觀與碰撞**、**計算關節運動學**、**計算關節限制**。採用載入值的群組不打勾，匯出時保留這些值；
   勾選則改由匯出器依模型重新計算該群組。維持現有值的群組會顯示為已勾選且灰色不可更改。確認後照常匯出。

SolidWorks 元件不會從 CSV 取得，一律來自模型。只存在於樹中的連桿保留原值。

### 常見問題

- *完全沒有配對*：連桿名稱不同。在合併視窗改 CSV 連桿的名稱，或直接改 CSV 內的名稱。
- *出現「此模型尚未完整匯出並儲存」提示*：目前連桿樹仍有未填完必要欄位的連桿。可以繼續，之後請檢查結果。
- *合併失敗*：CSV 損毀（缺標題列、數值欄有文字）。請重新匯出或修正檔案。
- 連桿名稱區分大小寫，且不可重複。
