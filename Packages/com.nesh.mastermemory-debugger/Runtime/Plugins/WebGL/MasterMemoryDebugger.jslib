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

  // Persists Application.persistentDataPath (IDBFS) to IndexedDB.
  MMDebugger_SyncFileSystem: function () {
    if (typeof FS === "undefined" || !FS.syncfs) return;
    FS.syncfs(false, function (err) {
      if (err) console.warn("[MasterMemoryDebugger] FS.syncfs failed", err);
    });
  }
});
