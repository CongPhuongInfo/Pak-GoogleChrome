Imports System
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Collections.Generic
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Drawing.Imaging

Module Program

    <STAThread()>
    Sub Main(args As String())
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Dim frm As New MainForm()
        If args.Length > 0 AndAlso File.Exists(args(0)) Then
            frm.LoadPackFile(args(0))
        End If
        Application.Run(frm)
    End Sub

End Module

''' <summary>
''' Một resource (file) nằm trong gói .pack
''' </summary>
Public Class PackEntry
    Public Property ResourceId As UShort
    Public Property Data As Byte()

    Public ReadOnly Property Length As Integer
        Get
            Return If(Data Is Nothing, 0, Data.Length)
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return String.Format("f_{0:X4}   ({1:N0} bytes)", ResourceId, Length)
    End Function
End Class

''' <summary>
''' Một alias trỏ tới 1 entry (resource) đã tồn tại, theo vị trí (entryIndex) trong bảng entries
''' </summary>
Public Class AliasEntry
    Public Property ResourceId As UShort
    Public Property Target As PackEntry

    Public Overrides Function ToString() As String
        Dim targetText As String
        If Target Is Nothing Then
            targetText = "(missing)"
        Else
            targetText = String.Format("f_{0:X4}", Target.ResourceId)
        End If
        Return String.Format("alias_{0:X4}   -> {1}", ResourceId, targetText)
    End Function
End Class

Public Class MainForm
    Inherits Form

    ' ── Cây bên trái ──
    Private splitMain As SplitContainer
    Private splitRight As SplitContainer
    Private tv As TreeView

    ' ── Toolbar ──
    Private toolStrip As ToolStrip

    ' ── Preview panel ──
    Private tabPreview As TabControl
    Private tabImage As TabPage
    Private tabText As TabPage
    Private tabHex As TabPage
    Private picBox As PictureBox
    Private txtPreview As RichTextBox
    Private txtHex As RichTextBox

    ' ── Log ──
    Private lstLog As ListBox

    ' ── Status bar ──
    Private statusBar As StatusStrip
    Private lblStatus As ToolStripStatusLabel

    ' ── Các nút Toolbar ──
    Private WithEvents btnOpen As ToolStripButton
    Private WithEvents btnSave As ToolStripButton
    Private WithEvents btnSaveAs As ToolStripButton
    Private WithEvents btnAdd As ToolStripButton
    Private WithEvents btnDelete As ToolStripButton
    Private WithEvents btnExtract As ToolStripButton
    Private WithEvents btnExtractAll As ToolStripButton

    Private nodeResources As TreeNode
    Private nodeAliases As TreeNode

    Private entries As New List(Of PackEntry)
    Private aliases As New List(Of AliasEntry)

    Private packVersion As UInteger = 1
    Private packEncoding As Byte = 0
    Private reservedBytes As Byte() = New Byte() {0, 0, 0}
    Private sentinelResourceId As UShort = 0
    Private currentPath As String = Nothing
    Private isDirty As Boolean = False

    ' ── Magic bytes để nhận dạng ──
    Private Shared ReadOnly SIG_PNG As Byte() = {&H89, &H50, &H4E, &H47}
    Private Shared ReadOnly SIG_JPG As Byte() = {&HFF, &HD8, &HFF}
    Private Shared ReadOnly SIG_GIF As Byte() = {&H47, &H49, &H46}
    Private Shared ReadOnly SIG_BMP As Byte() = {&H42, &H4D}
    Private Shared ReadOnly SIG_ICO As Byte() = {0, 0, 1, 0}
    Private Shared ReadOnly SIG_WEBP As Byte() = {&H52, &H49, &H46, &H46}

    Public Sub New()
        Me.Text = "Pack Editor  —  2CongLC"
        Me.Width = 1000
        Me.Height = 640
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(750, 480)
        Me.Font = New Font("Segoe UI", 9.0F)
        Me.BackColor = Color.FromArgb(240, 240, 240)

        BuildToolStrip()
        BuildStatusBar()
        BuildLayout()

        nodeResources = New TreeNode("Resources")
        nodeAliases = New TreeNode("Aliases")
        tv.Nodes.Add(nodeResources)
        tv.Nodes.Add(nodeAliases)

        UpdateStatus("Sẵn sàng.")
    End Sub

    ' =========================================================
    '  Xây dựng UI
    ' =========================================================

    Private Sub BuildToolStrip()
        toolStrip = New ToolStrip()
        toolStrip.GripStyle = ToolStripGripStyle.Hidden
        toolStrip.BackColor = Color.FromArgb(45, 45, 48)
        toolStrip.Renderer = New DarkRenderer()

        btnOpen      = MakeTSButton("📂 Mở", "Mở file .pack...")
        btnSave      = MakeTSButton("💾 Lưu", "Lưu file hiện tại")
        btnSaveAs    = MakeTSButton("💾 Lưu thành...", "Lưu với tên khác")
        toolStrip.Items.Add(New ToolStripSeparator())
        btnAdd       = MakeTSButton("➕ Thêm", "Thêm file vào gói")
        btnDelete    = MakeTSButton("🗑 Xóa", "Xóa mục đã chọn")
        toolStrip.Items.Add(New ToolStripSeparator())
        btnExtract   = MakeTSButton("⬇ Trích xuất", "Trích xuất mục đã chọn")
        btnExtractAll = MakeTSButton("⬇ Trích tất cả", "Trích xuất tất cả resources")

        Me.Controls.Add(toolStrip)
    End Sub

    Private Function MakeTSButton(caption As String, tip As String) As ToolStripButton
        Dim b As New ToolStripButton(caption)
        b.ToolTipText = tip
        b.ForeColor = Color.White
        b.Margin = New Padding(2, 0, 2, 0)
        b.Padding = New Padding(6, 0, 6, 0)
        toolStrip.Items.Add(b)
        Return b
    End Function

    Private Sub BuildStatusBar()
        statusBar = New StatusStrip()
        statusBar.BackColor = Color.FromArgb(0, 122, 204)
        lblStatus = New ToolStripStatusLabel("Sẵn sàng.")
        lblStatus.ForeColor = Color.White
        lblStatus.Spring = True
        lblStatus.TextAlign = ContentAlignment.MiddleLeft
        statusBar.Items.Add(lblStatus)
        Me.Controls.Add(statusBar)
    End Sub

    Private Sub BuildLayout()
        ' SplitContainer ngoài: Cây trái | Bên phải
        splitMain = New SplitContainer()
        splitMain.Dock = DockStyle.Fill
        splitMain.SplitterWidth = 4
        splitMain.SplitterDistance = 260
        splitMain.BackColor = Color.FromArgb(200, 200, 200)
        Me.Controls.Add(splitMain)

        ' Cây file
        tv = New TreeView()
        tv.Dock = DockStyle.Fill
        tv.HideSelection = False
        tv.Font = New Font("Consolas", 9.0F)
        tv.BackColor = Color.FromArgb(30, 30, 30)
        tv.ForeColor = Color.FromArgb(220, 220, 220)
        tv.BorderStyle = BorderStyle.None
        tv.ItemHeight = 20
        AddHandler tv.AfterSelect, AddressOf tv_AfterSelect
        splitMain.Panel1.Controls.Add(tv)

        ' SplitContainer phải: Preview trên | Log dưới
        splitRight = New SplitContainer()
        splitRight.Dock = DockStyle.Fill
        splitRight.Orientation = Orientation.Horizontal
        splitRight.SplitterWidth = 4
        splitRight.SplitterDistance = 380
        splitRight.BackColor = Color.FromArgb(200, 200, 200)
        splitMain.Panel2.Controls.Add(splitRight)

        BuildPreviewTabs()

        ' Log
        lstLog = New ListBox()
        lstLog.Dock = DockStyle.Fill
        lstLog.BorderStyle = BorderStyle.None
        lstLog.Font = New Font("Consolas", 8.5F)
        lstLog.BackColor = Color.FromArgb(20, 20, 20)
        lstLog.ForeColor = Color.FromArgb(180, 255, 180)
        splitRight.Panel2.Controls.Add(lstLog)

        Dim lblLog As New Label()
        lblLog.Text = " LOG"
        lblLog.Dock = DockStyle.Top
        lblLog.Height = 20
        lblLog.BackColor = Color.FromArgb(45, 45, 48)
        lblLog.ForeColor = Color.FromArgb(180, 180, 180)
        lblLog.Font = New Font("Segoe UI", 8.0F, FontStyle.Bold)
        splitRight.Panel2.Controls.Add(lblLog)
    End Sub

    Private Sub BuildPreviewTabs()
        tabPreview = New TabControl()
        tabPreview.Dock = DockStyle.Fill
        tabPreview.Font = New Font("Segoe UI", 9.0F)
        splitRight.Panel1.Controls.Add(tabPreview)

        ' Tab ảnh
        tabImage = New TabPage("🖼 Ảnh")
        picBox = New PictureBox()
        picBox.Dock = DockStyle.Fill
        picBox.SizeMode = PictureBoxSizeMode.Zoom
        picBox.BackColor = Color.FromArgb(30, 30, 30)
        tabImage.Controls.Add(picBox)
        tabPreview.TabPages.Add(tabImage)

        ' Tab text
        tabText = New TabPage("📄 Văn bản")
        txtPreview = New RichTextBox()
        txtPreview.Dock = DockStyle.Fill
        txtPreview.ReadOnly = True
        txtPreview.Font = New Font("Consolas", 9.5F)
        txtPreview.BackColor = Color.FromArgb(30, 30, 30)
        txtPreview.ForeColor = Color.FromArgb(220, 220, 220)
        txtPreview.BorderStyle = BorderStyle.None
        txtPreview.WordWrap = False
        txtPreview.ScrollBars = RichTextBoxScrollBars.Both
        tabText.Controls.Add(txtPreview)
        tabPreview.TabPages.Add(tabText)

        ' Tab hex
        tabHex = New TabPage("🔢 Hex")
        txtHex = New RichTextBox()
        txtHex.Dock = DockStyle.Fill
        txtHex.ReadOnly = True
        txtHex.Font = New Font("Consolas", 9.0F)
        txtHex.BackColor = Color.FromArgb(20, 20, 20)
        txtHex.ForeColor = Color.FromArgb(180, 220, 255)
        txtHex.BorderStyle = BorderStyle.None
        txtHex.WordWrap = False
        txtHex.ScrollBars = RichTextBoxScrollBars.Both
        tabHex.Controls.Add(txtHex)
        tabPreview.TabPages.Add(tabHex)
    End Sub

    ' =========================================================
    '  Preview khi chọn node
    ' =========================================================

    Private Sub tv_AfterSelect(sender As Object, e As TreeViewEventArgs)
        If e.Node Is Nothing OrElse Not TypeOf e.Node.Tag Is PackEntry Then
            ClearPreview()
            Return
        End If

        Dim en As PackEntry = CType(e.Node.Tag, PackEntry)
        Dim data As Byte() = en.Data

        If data Is Nothing OrElse data.Length = 0 Then
            ClearPreview()
            UpdateStatus(String.Format("f_{0:X4}  —  rỗng", en.ResourceId))
            Return
        End If

        UpdateStatus(String.Format("f_{0:X4}  —  {1:N0} bytes  —  {2}", en.ResourceId, data.Length, DetectTypeName(data)))

        ' ── Thử hiển thị ảnh ──
        Dim img As Image = TryLoadImage(data)
        If img IsNot Nothing Then
            picBox.Image = img
            tabPreview.SelectedTab = tabImage
            ShowHex(data)
            Return
        End If

        picBox.Image = Nothing

        ' ── Thử text (UTF-8) ──
        If IsLikelyText(data) Then
            Dim txt As String = Encoding.UTF8.GetString(data)
            txtPreview.Text = txt
            tabPreview.SelectedTab = tabText
        Else
            txtPreview.Text = "(Không phải văn bản)"
            tabPreview.SelectedTab = tabHex
        End If

        ShowHex(data)
    End Sub

    Private Sub ClearPreview()
        picBox.Image = Nothing
        txtPreview.Text = ""
        txtHex.Text = ""
    End Sub

    Private Function TryLoadImage(data As Byte()) As Image
        If data.Length < 4 Then Return Nothing
        If Not (StartsWith(data, SIG_PNG) OrElse StartsWith(data, SIG_JPG) OrElse
                StartsWith(data, SIG_GIF) OrElse StartsWith(data, SIG_BMP) OrElse
                StartsWith(data, SIG_ICO) OrElse StartsWith(data, SIG_WEBP)) Then
            Return Nothing
        End If
        Try
            Using ms As New MemoryStream(data)
                Return Image.FromStream(ms, False, False)
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    Private Function IsLikelyText(data As Byte()) As Boolean
        Dim sampleLen As Integer = Math.Min(data.Length, 2048)
        Dim ctrlCount As Integer = 0
        For i As Integer = 0 To sampleLen - 1
            Dim b As Byte = data(i)
            If b < 9 OrElse (b > 13 AndAlso b < 32) Then
                ctrlCount += 1
            End If
        Next
        Return (ctrlCount * 10 < sampleLen) ' < 10% ký tự điều khiển
    End Function

    Private Sub ShowHex(data As Byte())
        Dim maxBytes As Integer = Math.Min(data.Length, 8192) ' giới hạn 8KB để không chậm
        Dim sb As New StringBuilder()
        sb.AppendLine(String.Format("  Offset    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII"))
        sb.AppendLine(String.Format("  --------  -----------------------------------------------  ----------------"))
        Dim i As Integer = 0
        While i < maxBytes
            sb.Append(String.Format("  {0:X8}  ", i))
            Dim ascii As New StringBuilder()
            For j As Integer = 0 To 15
                If i + j < maxBytes Then
                    Dim b As Byte = data(i + j)
                    sb.Append(String.Format("{0:X2} ", b))
                    If j = 7 Then sb.Append(" ")
                    ascii.Append(If(b >= 32 AndAlso b < 127, Chr(b), "."))
                Else
                    sb.Append("   ")
                    If j = 7 Then sb.Append(" ")
                    ascii.Append(" ")
                End If
            Next
            sb.AppendLine(" " & ascii.ToString())
            i += 16
        End While
        If data.Length > maxBytes Then
            sb.AppendLine(String.Format("  ... (còn {0:N0} bytes, chỉ hiện {1:N0} bytes đầu)", data.Length - maxBytes, maxBytes))
        End If
        txtHex.Text = sb.ToString()
    End Sub

    Private Function DetectTypeName(data As Byte()) As String
        If data.Length < 2 Then Return "unknown"
        If StartsWith(data, SIG_PNG) Then Return "PNG image"
        If StartsWith(data, SIG_JPG) Then Return "JPEG image"
        If StartsWith(data, SIG_GIF) Then Return "GIF image"
        If StartsWith(data, SIG_BMP) Then Return "BMP image"
        If StartsWith(data, SIG_ICO) Then Return "ICO image"
        If data.Length >= 12 AndAlso StartsWith(data, SIG_WEBP) Then Return "WebP image"
        If data.Length >= 4 AndAlso data(0) = &H25 AndAlso data(1) = &H50 AndAlso data(2) = &H44 AndAlso data(3) = &H46 Then Return "PDF"
        If IsLikelyText(data) Then Return "text"
        Return "binary"
    End Function

    Private Shared Function StartsWith(data As Byte(), sig As Byte()) As Boolean
        If data.Length < sig.Length Then Return False
        For i As Integer = 0 To sig.Length - 1
            If data(i) <> sig(i) Then Return False
        Next
        Return True
    End Function

    ' =========================================================
    '  Toolbar actions
    ' =========================================================

    Private Sub btnOpen_Click(sender As Object, e As EventArgs) Handles btnOpen.Click
        Using ofd As New OpenFileDialog()
            ofd.Filter = "Pack files (*.pak)|*.pak|All files (*.*)|*.*"
            If ofd.ShowDialog(Me) = DialogResult.OK Then
                LoadPackFile(ofd.FileName)
            End If
        End Using
    End Sub

    Public Sub LoadPackFile(path As String)
        Try
            entries.Clear()
            aliases.Clear()

            Using br As New BinaryReader(File.OpenRead(path))
                packVersion = br.ReadUInt32()
                packEncoding = br.ReadByte()
                reservedBytes = br.ReadBytes(3)
                Dim resourceCount As UShort = br.ReadUInt16()
                Dim aliasCount As UShort = br.ReadUInt16()

                Dim rawIds(resourceCount) As UShort
                Dim rawOffsets(resourceCount) As UInteger
                For i As Integer = 0 To resourceCount
                    rawIds(i) = br.ReadUInt16()
                    rawOffsets(i) = br.ReadUInt32()
                Next
                sentinelResourceId = rawIds(resourceCount)

                Dim aliasIds(If(aliasCount > 0, aliasCount - 1, -1)) As UShort
                Dim aliasIdx(If(aliasCount > 0, aliasCount - 1, -1)) As UShort
                For i As Integer = 0 To aliasCount - 1
                    aliasIds(i) = br.ReadUInt16()
                    aliasIdx(i) = br.ReadUInt16()
                Next

                For i As Integer = 0 To resourceCount - 1
                    br.BaseStream.Seek(rawOffsets(i), SeekOrigin.Begin)
                    Dim length As Integer = CInt(rawOffsets(i + 1) - rawOffsets(i))
                    Dim buff(If(length > 0, length - 1, 0)) As Byte
                    If length > 0 Then br.Read(buff, 0, length)
                    entries.Add(New PackEntry() With {.ResourceId = rawIds(i), .Data = buff})
                Next

                For i As Integer = 0 To aliasCount - 1
                    Dim targetEntry As PackEntry = If(aliasIdx(i) < entries.Count, entries(aliasIdx(i)), Nothing)
                    aliases.Add(New AliasEntry() With {.ResourceId = aliasIds(i), .Target = targetEntry})
                Next
            End Using

            currentPath = path
            isDirty = False
            RefreshTree()
            Me.Text = "Pack Editor  —  " & IO.Path.GetFileName(path)
            Log(String.Format("[OK] Mở: {0}  ({1} resources, {2} aliases)", path, entries.Count, aliases.Count))
            UpdateStatus(String.Format("{0} resources, {1} aliases  |  {2}", entries.Count, aliases.Count, IO.Path.GetFileName(path)))
        Catch ex As Exception
            Log("[ERR] " & ex.Message)
            MessageBox.Show(Me, "Không mở được: " & ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub RefreshTree()
        tv.BeginUpdate()
        nodeResources.Nodes.Clear()
        nodeAliases.Nodes.Clear()

        For Each en As PackEntry In entries
            Dim n As New TreeNode(en.ToString())
            n.Tag = en
            nodeResources.Nodes.Add(n)
        Next

        For Each al As AliasEntry In aliases
            Dim n As New TreeNode(al.ToString())
            n.Tag = al
            nodeAliases.Nodes.Add(n)
        Next

        nodeResources.Text = String.Format("Resources ({0})", entries.Count)
        nodeAliases.Text = String.Format("Aliases ({0})", aliases.Count)
        nodeResources.Expand()
        nodeAliases.Expand()
        tv.EndUpdate()
    End Sub

    ' ── Thêm ──

    Private Sub btnAdd_Click(sender As Object, e As EventArgs) Handles btnAdd.Click
        Using ofd As New OpenFileDialog()
            ofd.Multiselect = True
            ofd.Filter = "All files (*.*)|*.*"
            If ofd.ShowDialog(Me) = DialogResult.OK Then
                For Each f As String In ofd.FileNames
                    Try
                        Dim newId As UShort = NextFreeResourceId()
                        Dim data As Byte() = File.ReadAllBytes(f)
                        entries.Add(New PackEntry() With {.ResourceId = newId, .Data = data})
                        isDirty = True
                        Log(String.Format("[ADD] '{0}' -> f_{1:X4} ({2:N0} bytes)", IO.Path.GetFileName(f), newId, data.Length))
                    Catch ex As Exception
                        MessageBox.Show(Me, "Không thêm được '" & f & "': " & ex.Message)
                    End Try
                Next
                RefreshTree()
            End If
        End Using
    End Sub

    Private Function NextFreeResourceId() As UShort
        Dim used As New HashSet(Of UShort)(entries.Select(Function(x) x.ResourceId))
        Dim id As UInteger = 0
        While used.Contains(CUShort(id))
            id = id + 1UI
        End While
        Return CUShort(id)
    End Function

    ' ── Xóa ──

    Private Sub btnDelete_Click(sender As Object, e As EventArgs) Handles btnDelete.Click
        If tv.SelectedNode Is Nothing Then
            MessageBox.Show(Me, "Chọn một mục trong cây trước.")
            Return
        End If

        Dim tag = tv.SelectedNode.Tag
        If TypeOf tag Is PackEntry Then
            Dim en As PackEntry = CType(tag, PackEntry)
            Dim affected = aliases.Where(Function(a) a.Target Is en).ToList()
            If affected.Count > 0 Then
                Dim r As DialogResult = MessageBox.Show(Me,
                    String.Format("Có {0} alias đang trỏ tới f_{1:X4}. Xóa luôn các alias này?", affected.Count, en.ResourceId),
                    "Xác nhận", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning)
                If r = DialogResult.Cancel Then Return
                If r = DialogResult.Yes Then
                    For Each a In affected
                        aliases.Remove(a)
                    Next
                End If
            End If
            entries.Remove(en)
            isDirty = True
            Log(String.Format("[DEL] f_{0:X4}", en.ResourceId))
            ClearPreview()
            RefreshTree()
        ElseIf TypeOf tag Is AliasEntry Then
            Dim al As AliasEntry = CType(tag, AliasEntry)
            aliases.Remove(al)
            isDirty = True
            Log(String.Format("[DEL] alias_{0:X4}", al.ResourceId))
            RefreshTree()
        Else
            MessageBox.Show(Me, "Hãy chọn một resource hoặc alias cụ thể (không phải nhóm).")
        End If
    End Sub

    ' ── Trích xuất ──

    Private Sub btnExtract_Click(sender As Object, e As EventArgs) Handles btnExtract.Click
        If tv.SelectedNode Is Nothing OrElse Not TypeOf tv.SelectedNode.Tag Is PackEntry Then
            MessageBox.Show(Me, "Chọn một resource cụ thể trong cây trước.")
            Return
        End If
        Dim en As PackEntry = CType(tv.SelectedNode.Tag, PackEntry)
        Using fbd As New FolderBrowserDialog()
            If fbd.ShowDialog(Me) = DialogResult.OK Then
                Dim outPath As String = Path.Combine(fbd.SelectedPath, String.Format("f_{0:X4}", en.ResourceId))
                File.WriteAllBytes(outPath, en.Data)
                Log(String.Format("[OUT] -> {0}", outPath))
            End If
        End Using
    End Sub

    Private Sub btnExtractAll_Click(sender As Object, e As EventArgs) Handles btnExtractAll.Click
        If entries.Count = 0 Then
            MessageBox.Show(Me, "Chưa có resource nào.")
            Return
        End If
        Using fbd As New FolderBrowserDialog()
            If fbd.ShowDialog(Me) = DialogResult.OK Then
                For Each en In entries
                    Dim outPath As String = Path.Combine(fbd.SelectedPath, String.Format("f_{0:X4}", en.ResourceId))
                    File.WriteAllBytes(outPath, en.Data)
                Next
                Log(String.Format("[OUT] Đã trích {0} file -> {1}", entries.Count, fbd.SelectedPath))
            End If
        End Using
    End Sub

    ' ── Lưu ──

    Private Sub btnSave_Click(sender As Object, e As EventArgs) Handles btnSave.Click
        If String.IsNullOrEmpty(currentPath) Then
            btnSaveAs_Click(sender, e)
        Else
            SavePackFile(currentPath)
        End If
    End Sub

    Private Sub btnSaveAs_Click(sender As Object, e As EventArgs) Handles btnSaveAs.Click
        Using sfd As New SaveFileDialog()
            sfd.Filter = "Pack files (*.pak)|*.pak|All files (*.*)|*.*"
            If Not String.IsNullOrEmpty(currentPath) Then sfd.FileName = currentPath
            If sfd.ShowDialog(Me) = DialogResult.OK Then
                SavePackFile(sfd.FileName)
                currentPath = sfd.FileName
            End If
        End Using
    End Sub

    Private Sub SavePackFile(path As String)
        Try
            Dim resourceCount As Integer = entries.Count
            Dim aliasCount As Integer = aliases.Count

            Dim headerSize As Integer = 4 + 1 + 3 + 2 + 2
            Dim entriesTableSize As Integer = (resourceCount + 1) * (2 + 4)
            Dim aliasesTableSize As Integer = aliasCount * (2 + 2)
            Dim dataStart As UInteger = CUInt(headerSize + entriesTableSize + aliasesTableSize)

            Dim offsets(resourceCount) As UInteger
            Dim running As UInteger = dataStart
            For i As Integer = 0 To resourceCount - 1
                offsets(i) = running
                running = running + CUInt(entries(i).Length)
            Next
            offsets(resourceCount) = running

            Using bw As New BinaryWriter(File.Create(path))
                bw.Write(packVersion)
                bw.Write(packEncoding)
                bw.Write(reservedBytes)
                bw.Write(CUShort(resourceCount))
                bw.Write(CUShort(aliasCount))

                For i As Integer = 0 To resourceCount - 1
                    bw.Write(entries(i).ResourceId)
                    bw.Write(offsets(i))
                Next
                bw.Write(sentinelResourceId)
                bw.Write(offsets(resourceCount))

                For Each al As AliasEntry In aliases
                    Dim idx As Integer = If(al.Target Is Nothing, 0, entries.IndexOf(al.Target))
                    bw.Write(al.ResourceId)
                    bw.Write(CUShort(Math.Max(idx, 0)))
                Next

                For Each en As PackEntry In entries
                    bw.Write(en.Data)
                Next
            End Using

            isDirty = False
            Log(String.Format("[OK] Đã lưu: {0}", path))
            UpdateStatus("Đã lưu: " & IO.Path.GetFileName(path))
        Catch ex As Exception
            MessageBox.Show(Me, "Lưu thất bại: " & ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' =========================================================
    '  Helper
    ' =========================================================

    Private Sub Log(msg As String)
        lstLog.Items.Add(msg)
        lstLog.TopIndex = lstLog.Items.Count - 1
    End Sub

    Private Sub UpdateStatus(msg As String)
        lblStatus.Text = "  " & msg
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If isDirty Then
            Dim r As DialogResult = MessageBox.Show(Me, "Có thay đổi chưa lưu. Đóng luôn?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If r = DialogResult.No Then
                e.Cancel = True
                MyBase.OnFormClosing(e)
                Return
            End If
        End If
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        ' Thiết lập SplitterDistance sau khi form đã hiển thị đúng kích thước
        splitRight.SplitterDistance = Math.Max(200, splitRight.Height - 160)
    End Sub

End Class

' =========================================================
'  Custom dark renderer cho ToolStrip
' =========================================================

Public Class DarkRenderer
    Inherits ToolStripProfessionalRenderer

    Protected Overrides Sub OnRenderToolStripBackground(e As ToolStripRenderEventArgs)
        e.Graphics.FillRectangle(New SolidBrush(Color.FromArgb(45, 45, 48)), e.AffectedBounds)
    End Sub

    Protected Overrides Sub OnRenderButtonBackground(e As ToolStripItemRenderEventArgs)
        Dim btn = TryCast(e.Item, ToolStripButton)
        If btn IsNot Nothing AndAlso btn.Pressed Then
            e.Graphics.FillRectangle(New SolidBrush(Color.FromArgb(0, 122, 204)), New Rectangle(Point.Empty, e.Item.Size))
        ElseIf btn IsNot Nothing AndAlso btn.Selected Then
            e.Graphics.FillRectangle(New SolidBrush(Color.FromArgb(62, 62, 64)), New Rectangle(Point.Empty, e.Item.Size))
        End If
    End Sub

    Protected Overrides Sub OnRenderSeparator(e As ToolStripSeparatorRenderEventArgs)
        Dim x As Integer = e.Item.Width \ 2
        e.Graphics.DrawLine(New Pen(Color.FromArgb(80, 80, 80)), x, 4, x, e.Item.Height - 4)
    End Sub
End Class
