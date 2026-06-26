Imports System
Imports System.IO
Imports System.Linq
Imports System.Collections.Generic
Imports System.Windows.Forms
Imports System.Drawing

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

    Private tv As TreeView
    Private WithEvents btnOpen As Button
    Private WithEvents btnSave As Button
    Private WithEvents btnSaveAs As Button
    Private WithEvents btnAdd As Button
    Private WithEvents btnDelete As Button
    Private WithEvents btnExtract As Button
    Private WithEvents btnExtractAll As Button
    Private lstLog As ListBox

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

    Public Sub New()
        Me.Text = "2CongLC Pack Editor"
        Me.Width = 780
        Me.Height = 560
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.MinimumSize = New Size(620, 420)

        Dim layout As New TableLayoutPanel()
        layout.Dock = DockStyle.Fill
        layout.ColumnCount = 2
        layout.RowCount = 1
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 430))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        Me.Controls.Add(layout)

        tv = New TreeView()
        tv.Dock = DockStyle.Fill
        tv.HideSelection = False
        layout.Controls.Add(tv, 0, 0)

        Dim panel As New Panel()
        panel.Dock = DockStyle.Fill
        layout.Controls.Add(panel, 1, 0)

        btnOpen = MakeButton("Mở gói (.pack)...", 10)
        btnSave = MakeButton("Lưu", 50)
        btnSaveAs = MakeButton("Lưu thành...", 90)
        btnAdd = MakeButton("Thêm file...", 140)
        btnDelete = MakeButton("Xóa mục đã chọn", 180)
        btnExtract = MakeButton("Trích xuất mục đã chọn...", 220)
        btnExtractAll = MakeButton("Trích xuất tất cả...", 260)

        panel.Controls.Add(btnOpen)
        panel.Controls.Add(btnSave)
        panel.Controls.Add(btnSaveAs)
        panel.Controls.Add(btnAdd)
        panel.Controls.Add(btnDelete)
        panel.Controls.Add(btnExtract)
        panel.Controls.Add(btnExtractAll)

        lstLog = New ListBox()
        lstLog.Top = 310
        lstLog.Left = 10
        lstLog.Width = 320
        lstLog.Height = 200
        lstLog.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Bottom
        panel.Controls.Add(lstLog)

        nodeResources = New TreeNode("Resources")
        nodeAliases = New TreeNode("Aliases")
        tv.Nodes.Add(nodeResources)
        tv.Nodes.Add(nodeAliases)
    End Sub

    Private Function MakeButton(caption As String, top As Integer) As Button
        Dim b As New Button()
        b.Text = caption
        b.Left = 10
        b.Top = top
        b.Width = 320
        Return b
    End Function

    Private Sub Log(msg As String)
        lstLog.Items.Add(msg)
        lstLog.TopIndex = lstLog.Items.Count - 1
    End Sub

    ' ===================== Mở / phân tích gói =====================

    Private Sub btnOpen_Click(sender As Object, e As EventArgs) Handles btnOpen.Click
        Using ofd As New OpenFileDialog()
            ofd.Filter = "Pack files (*.pack)|*.pack|All files (*.*)|*.*"
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

                ' Bảng entries có resourceCount + 1 phần tử: phần tử cuối chỉ dùng làm "mốc kết thúc"
                ' để tính độ dài của entry cuối cùng, không phải 1 resource thật.
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
            Log(String.Format("Đã mở: {0}  ({1} resources, {2} aliases)", path, entries.Count, aliases.Count))
        Catch ex As Exception
            MessageBox.Show(Me, "Không đọc được file: " & ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub RefreshTree()
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
    End Sub

    ' ===================== Thêm =====================

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
                        Log(String.Format("Đã thêm '{0}' làm f_{1:X4} ({2:N0} bytes)", Path.GetFileName(f), newId, data.Length))
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

    ' ===================== Xóa =====================

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
            Log(String.Format("Đã xóa f_{0:X4}", en.ResourceId))
            RefreshTree()
        ElseIf TypeOf tag Is AliasEntry Then
            Dim al As AliasEntry = CType(tag, AliasEntry)
            aliases.Remove(al)
            isDirty = True
            Log(String.Format("Đã xóa alias_{0:X4}", al.ResourceId))
            RefreshTree()
        Else
            MessageBox.Show(Me, "Hãy chọn một resource hoặc alias cụ thể (không phải nhóm).")
        End If
    End Sub

    ' ===================== Trích xuất =====================

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
                Log(String.Format("Đã trích xuất -> {0}", outPath))
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
                Log(String.Format("Đã trích xuất {0} file vào {1}", entries.Count, fbd.SelectedPath))
            End If
        End Using
    End Sub

    ' ===================== Lưu (đóng gói lại) =====================

    Private Sub btnSave_Click(sender As Object, e As EventArgs) Handles btnSave.Click
        If String.IsNullOrEmpty(currentPath) Then
            btnSaveAs_Click(sender, e)
        Else
            SavePackFile(currentPath)
        End If
    End Sub

    Private Sub btnSaveAs_Click(sender As Object, e As EventArgs) Handles btnSaveAs.Click
        Using sfd As New SaveFileDialog()
            sfd.Filter = "Pack files (*.pack)|*.pack|All files (*.*)|*.*"
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

            ' Vị trí bắt đầu của vùng data, ngay sau header + bảng entries (resourceCount+1) + bảng aliases
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
            offsets(resourceCount) = running ' mốc kết thúc data

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
            Log(String.Format("Đã lưu: {0}", path))
        Catch ex As Exception
            MessageBox.Show(Me, "Lưu thất bại: " & ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
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

End Class
