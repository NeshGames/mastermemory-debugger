mergeInto(LibraryManager.library, {
  // Starts a browser download of a text file.
  MMDebugger_DownloadFile: function (fileNamePtr, contentPtr, mimeTypePtr) {
    var fileName = UTF8ToString(fileNamePtr);
    var content = UTF8ToString(contentPtr);
    var mimeType = UTF8ToString(mimeTypePtr) || "application/json";
    var blob = new Blob([content], { type: mimeType + ";charset=utf-8" });
    var url = URL.createObjectURL(blob);
    var link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  },

  // Opens the browser file picker and passes "<file name>\n<file text>" to the C# callback.
  // Must be called while handling a user click (browsers block file pickers otherwise).
  MMDebugger_OpenFile: function (acceptPtr, callback) {
    var accept = UTF8ToString(acceptPtr);
    var input = document.createElement("input");
    input.type = "file";
    input.accept = accept;
    input.style.display = "none";
    input.onchange = function () {
      var file = input.files && input.files[0];
      if (input.parentNode) input.parentNode.removeChild(input);
      if (!file) return;
      var reader = new FileReader();
      reader.onload = function () {
        var text = file.name + "\n" + reader.result;
        var size = lengthBytesUTF8(text) + 1;
        var buffer = _malloc(size);
        stringToUTF8(text, buffer, size);
        {{{ makeDynCall('vi', 'callback') }}}(buffer);
        _free(buffer);
      };
      reader.readAsText(file);
    };
    document.body.appendChild(input);
    input.click();
  },

  // Persists Application.persistentDataPath (IDBFS) to IndexedDB.
  MMDebugger_SyncFileSystem: function () {
    if (typeof FS === "undefined" || !FS.syncfs) return;
    FS.syncfs(false, function (err) {
      if (err) console.warn("[MasterMemoryDebugger] FS.syncfs failed", err);
    });
  }
});
