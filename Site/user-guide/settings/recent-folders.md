# Recently Opened

Recently Opened appears directly below History in Settings. It records filesystem folders visited in the foreground Windows File Explorer window and active tab, independently of searches. Files, background navigation, third-party file managers, file dialogs and virtual locations such as This PC are excluded.

- **Record folder visits** is on by default. Disabling it keeps saved entries and stops additions and timestamp updates.
- **Entries kept** defaults to 200 (1–5000). The oldest entries are discarded when the limit is reached.
- **Menu entries** defaults to 20 (1–100), no greater than the saved-entry limit.
- **Excluded folders** accepts one absolute path per line, including environment variables. Only the listed folder itself is excluded; its subfolders are not. Apply also removes matching saved entries. Wildcards are not supported.
- **Search and cleanup** lets you filter names and paths. Delete and clear take effect immediately. Recording options, limits and exclusions are saved with Apply. Visiting a deleted folder again may record it again.

Quick Navigation places Recently Opened after History, separated by a divider. Folders use the existing cascading submenus and navigation actions. The Folder Cascader plugin has an independent Show recently opened folders setting.

Each path appears once, most recently visited first. Switching windows or tabs counts as a visit; duplicate window events do not. Events settle for approximately 200 ms, so transient intermediate folders may not be captured. Startup captures only the current foreground folder.

Entries persist in `recent-folders.json` in the user data directory and do not expire by age. Temporarily inaccessible folders remain saved but are skipped in the menu. Updates are coalesced for approximately one second and written atomically; normal exit flushes pending updates. An unexpected process termination may lose the last unsaved updates.
