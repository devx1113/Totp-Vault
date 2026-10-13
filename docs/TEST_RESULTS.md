# テスト結果

- 実行日時: 2026-10-13 13:07:51（UTC+9）
- 実行環境: .NET 10.0 / xUnit 2.9
- 対象: `TotpVault.Core`（TOTP 生成・暗号化・保管庫・バックアップ・QR 読み取り・セキュリティ判定ロジック）
- 結果: **151 / 151 件成功（失敗 0 件）**

再実行方法: `dotnet test`（または `build.ps1`）

## 1. 自動テスト

| 区分 | 件数 | 結果 |
|---|---:|---|
| TOTP / HOTP 生成（RFC 6238・RFC 4226 テストベクター） | 40 | ✅ すべて成功 |
| Base32 デコード（RFC 4648） | 18 | ✅ すべて成功 |
| QR コード内容（otpauth URI・Google Authenticator 移行形式）の解析 | 22 | ✅ すべて成功 |
| AES-256-GCM 暗号化・改ざん検出 | 9 | ✅ すべて成功 |
| 鍵導出（PBKDF2-HMAC-SHA256） | 14 | ✅ すべて成功 |
| 暗号化保管庫（保存・ロック・改ざん検出・パスワード変更） | 16 | ✅ すべて成功 |
| 暗号化バックアップ・復元・別 PC 移行 | 12 | ✅ すべて成功 |
| QR コード画像の読み取り | 7 | ✅ すべて成功 |
| 自動ロック・連続失敗時の待機・クリップボード消去・パスワード強度 | 12 | ✅ すべて成功 |
| ログへの機密情報出力の防止 | 1 | ✅ すべて成功 |

### TOTP / HOTP 生成（RFC 6238・RFC 4226 テストベクター）

| テスト | 結果 |
|---|---|
| RFC 4226 HOTP テストベクター (6桁)(counter: 0, expected: "755224") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 1, expected: "287082") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 2, expected: "359152") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 3, expected: "969429") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 4, expected: "338314") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 5, expected: "254676") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 6, expected: "287922") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 7, expected: "162583") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 8, expected: "399871") | ✅ 成功 |
| RFC 4226 HOTP テストベクター (6桁)(counter: 9, expected: "520489") | ✅ 成功 |
| RFC 6238 テストベクター (6桁)(time: 1111111109, algorithm: Sha256, expected: "084774") | ✅ 成功 |
| RFC 6238 テストベクター (6桁)(time: 20000000000, algorithm: Sha512, expected: "863826") | ✅ 成功 |
| RFC 6238 テストベクター (6桁)(time: 59, algorithm: Sha1, expected: "287082") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1111111109, algorithm: Sha1, expected: "07081804") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1111111109, algorithm: Sha256, expected: "68084774") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1111111109, algorithm: Sha512, expected: "25091201") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1111111111, algorithm: Sha1, expected: "14050471") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1111111111, algorithm: Sha256, expected: "67062674") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1111111111, algorithm: Sha512, expected: "99943326") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1234567890, algorithm: Sha1, expected: "89005924") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1234567890, algorithm: Sha256, expected: "91819424") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 1234567890, algorithm: Sha512, expected: "93441116") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 2000000000, algorithm: Sha1, expected: "69279037") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 2000000000, algorithm: Sha256, expected: "90698825") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 2000000000, algorithm: Sha512, expected: "38618901") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 20000000000, algorithm: Sha1, expected: "65353130") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 20000000000, algorithm: Sha256, expected: "77737706") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 20000000000, algorithm: Sha512, expected: "47863826") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 59, algorithm: Sha1, expected: "94287082") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 59, algorithm: Sha256, expected: "46119246") | ✅ 成功 |
| RFC 6238 テストベクター (8桁, SHA-1/256/512)(time: 59, algorithm: Sha512, expected: "90693936") | ✅ 成功 |
| コード表示の区切り(code: "123456", expected: "123 456") | ✅ 成功 |
| コード表示の区切り(code: "1234567", expected: "123 4567") | ✅ 成功 |
| コード表示の区切り(code: "12345678", expected: "1234 5678") | ✅ 成功 |
| 不正な桁数は拒否する | ✅ 成功 |
| 更新間隔60秒ではカウンターが60秒ごとに進む | ✅ 成功 |
| 次の更新までの残り時間(nowMs: 119999, period: 60, expected: 1) | ✅ 成功 |
| 次の更新までの残り時間(nowMs: 59000, period: 30, expected: 1000) | ✅ 成功 |
| 次の更新までの残り時間(nowMs: 60000, period: 30, expected: 30000) | ✅ 成功 |
| 次の更新までの残り時間(nowMs: 60500, period: 30, expected: 29500) | ✅ 成功 |

### Base32 デコード（RFC 4648）

| テスト | 結果 |
|---|---|
| RFC 4648 テストベクター(plain: "f", encoded: "MY") | ✅ 成功 |
| RFC 4648 テストベクター(plain: "fo", encoded: "MZXQ") | ✅ 成功 |
| RFC 4648 テストベクター(plain: "foo", encoded: "MZXW6") | ✅ 成功 |
| RFC 4648 テストベクター(plain: "foob", encoded: "MZXW6YQ") | ✅ 成功 |
| RFC 4648 テストベクター(plain: "fooba", encoded: "MZXW6YTB") | ✅ 成功 |
| RFC 4648 テストベクター(plain: "foobar", encoded: "MZXW6YTBOI") | ✅ 成功 |
| 不正な Base32 は拒否(input: "   ") | ✅ 成功 |
| 不正な Base32 は拒否(input: "") | ✅ 成功 |
| 不正な Base32 は拒否(input: "MZ=XW6") | ✅ 成功 |
| 不正な Base32 は拒否(input: "MZXW1") | ✅ 成功 |
| 不正な Base32 は拒否(input: "MZXW6!") | ✅ 成功 |
| 不正な Base32 は拒否(input: "MZXW8YTB") | ✅ 成功 |
| 入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容(input: " mzxw\t6ytb\noi ") | ✅ 成功 |
| 入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容(input: "MZXW 6YTB OI") | ✅ 成功 |
| 入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容(input: "MZXW-6YTB-OI") | ✅ 成功 |
| 入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容(input: "MZXW6YTBOI======") | ✅ 成功 |
| 入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容(input: "mzxw6ytboi") | ✅ 成功 |
| 入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容(input: "ＭＺＸＷ６ＹＴＢＯＩ") | ✅ 成功 |

### QR コード内容（otpauth URI・Google Authenticator 移行形式）の解析

| テスト | 結果 |
|---|---|
| Google Authenticator 移行QR (otpauth-migration) を解析 | ✅ 成功 |
| HOTP は分かりやすいメッセージで拒否 | ✅ 成功 |
| URLエンコード・アルゴリズム・桁数・更新間隔を解析 | ✅ 成功 |
| issuer パラメータはラベルより優先 | ✅ 成功 |
| サービス名なしのラベル | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "https://example.com/") | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://hotp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?issuer=Example") | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DP") | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3P"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://totp/Example:alice?secret=NOT-BASE32-189"···) | ✅ 成功 |
| 不正な QR コード内容はエラー(uri: "otpauth://unknown/x?secret=JBSWY3DPEHPK3PXP") | ✅ 成功 |
| 壊れた移行データはエラー(uri: "otpauth-migration://offline") | ✅ 成功 |
| 壊れた移行データはエラー(uri: "otpauth-migration://offline?data=%%%") | ✅ 成功 |
| 壊れた移行データはエラー(uri: "otpauth-migration://offline?data=CgQKAg") | ✅ 成功 |
| 日本語のサービス名（UTF-8エンコード） | ✅ 成功 |
| 標準的な otpauth URI を解析 | ✅ 成功 |

### AES-256-GCM 暗号化・改ざん検出

| テスト | 結果 |
|---|---|
| AES-256-GCM 暗号化・復号の往復 | ✅ 成功 |
| 同じ平文でも毎回異なる暗号文（nonce の再利用なし） | ✅ 成功 |
| 暗号文のどの部分を改ざんしても検出(index: -1) | ✅ 成功 |
| 暗号文のどの部分を改ざんしても検出(index: 0) | ✅ 成功 |
| 暗号文のどの部分を改ざんしても検出(index: 14) | ✅ 成功 |
| 暗号文のどの部分を改ざんしても検出(index: 5) | ✅ 成功 |
| 短すぎるデータは拒否 | ✅ 成功 |
| 鍵が異なると復号できない | ✅ 成功 |
| 関連データ (AAD) が異なると復号できない（レコード差し替え対策） | ✅ 成功 |

### 鍵導出（PBKDF2-HMAC-SHA256）

| テスト | 結果 |
|---|---|
| PBKDF2 代替実装が OS 実装と同一の出力(password: "", salt: "saltSALTsaltSALT", iterations: 2, length: 64) | ✅ 成功 |
| PBKDF2 代替実装が OS 実装と同一の出力(password: "long", salt: "saltSALTsaltSALT", iterations: 3, length: 40) | ✅ 成功 |
| PBKDF2 代替実装が OS 実装と同一の出力(password: "password", salt: "saltSALTsaltSALT", iterations: 1, length: 32) | ✅ 成功 |
| PBKDF2 代替実装が OS 実装と同一の出力(password: "password", salt: "saltSALTsaltSALT", iterations: 4096, length: 32) | ✅ 成功 |
| PBKDF2 代替実装が OS 実装と同一の出力(password: "パスワード\ud83d\udd11", salt: "0123456789abcdef0123456789abcdef", iterations: 1000, length: 32) | ✅ 成功 |
| PBKDF2-HMAC-SHA256 公開テストベクター (c=4096) | ✅ 成功 |
| PBKDF2-HMAC-SHA256 既知ベクター | ✅ 成功 |
| PBKDF2: ソルトが異なれば別の鍵 | ✅ 成功 |
| PBKDF2: 同じパスワードとソルトなら同じ鍵 | ✅ 成功 |
| 既定の反復回数は 600,000 回 (OWASP 推奨) | ✅ 成功 |
| 異常な KDF パラメータは拒否（細工されたファイル対策）(alg: "md5", iterations: 600000, saltLength: 32) | ✅ 成功 |
| 異常な KDF パラメータは拒否（細工されたファイル対策）(alg: "pbkdf2-sha256", iterations: 1, saltLength: 32) | ✅ 成功 |
| 異常な KDF パラメータは拒否（細工されたファイル対策）(alg: "pbkdf2-sha256", iterations: 100000000, saltLength: 32) | ✅ 成功 |
| 異常な KDF パラメータは拒否（細工されたファイル対策）(alg: "pbkdf2-sha256", iterations: 600000, saltLength: 4) | ✅ 成功 |

### 暗号化保管庫（保存・ロック・改ざん検出・パスワード変更）

| テスト | 結果 |
|---|---|
| DBファイルに平文のシークレット・サービス名・アカウント名が含まれない | ✅ 成功 |
| DPAPI 層: 保護方式が異なる保管庫は開かない | ✅ 成功 |
| DPAPI 層: 別ユーザー/別PCでは解除できない | ✅ 成功 |
| KDF パラメータの改ざんを検出（解除不可） | ✅ 成功 |
| アカウントの編集・お気に入り・削除・並び替えが保存される | ✅ 成功 |
| マスターパスワード変更: 旧パスワード無効・新パスワード有効・データ鍵も更新 | ✅ 成功 |
| ロック中はコード生成・一覧取得ができない | ✅ 成功 |
| 保管庫から生成したコードが RFC 実装と一致 | ✅ 成功 |
| 保管庫の作成 → 再オープン → 正しいパスワードで解除 | ✅ 成功 |
| 入力値の検証（空の名前・短いシークレット・不正な桁数） | ✅ 成功 |
| 同じシークレットの重複登録を検出 | ✅ 成功 |
| 新しいバージョンで作成された保管庫は開かない | ✅ 成功 |
| 暗号化レコードの差し替え（行の入れ替え）を検出 | ✅ 成功 |
| 暗号文の1バイト改ざんを検出 | ✅ 成功 |
| 破損したファイル（DBではない）を開くとエラー | ✅ 成功 |
| 設定値の保存と読み込み | ✅ 成功 |

### 暗号化バックアップ・復元・別 PC 移行

| テスト | 結果 |
|---|---|
| バックアップの作成と復元（往復） | ✅ 成功 |
| バックアップファイルに平文の機密情報が含まれない | ✅ 成功 |
| パスワード誤りは BackupDecryptionException | ✅ 成功 |
| ヘッダーの改ざんを検出（作成日時・反復回数）(from: "\"createdAt\": \"20", to: "\"createdAt\": \"19") | ✅ 成功 |
| ヘッダーの改ざんを検出（作成日時・反復回数）(from: "\"iterations\": 1000", to: "\"iterations\": 1001") | ✅ 成功 |
| 保管庫 → バックアップ → 別の保管庫へ復元（別PC移行を想定）、重複はスキップ | ✅ 成功 |
| 形式違い・新しいバージョンのファイルは BackupFormatException(content: "not json at all") | ✅ 成功 |
| 形式違い・新しいバージョンのファイルは BackupFormatException(content: "{\"format\":\"TotpVault.Backup\",\"version\":1,\"k"···) | ✅ 成功 |
| 形式違い・新しいバージョンのファイルは BackupFormatException(content: "{\"format\":\"TotpVault.Backup\",\"version\":99}") | ✅ 成功 |
| 形式違い・新しいバージョンのファイルは BackupFormatException(content: "{\"hello\":\"world\"}") | ✅ 成功 |
| 暗号化データの改ざんを検出 | ✅ 成功 |
| 途中で切れた（破損した）ファイルは BackupFormatException | ✅ 成功 |

### QR コード画像の読み取り

| テスト | 結果 |
|---|---|
| QRコードのない画像は空の結果 | ✅ 成功 |
| QRコード画像から otpauth URI を読み取り、登録情報に変換 | ✅ 成功 |
| 画面キャプチャ内の複数の QR コードを読み取れる | ✅ 成功 |
| 画面キャプチャ（アルファ値が不定）は ignoreAlpha で読み取れる | ✅ 成功 |
| 画面キャプチャ（アルファ値が全て 0）の画像も読み取れる | ✅ 成功 |
| 白黒反転（ダークモード）の QR コードも読み取れる | ✅ 成功 |
| 背景透過の QR 画像は白背景に合成して読み取れる | ✅ 成功 |

### 自動ロック・連続失敗時の待機・クリップボード消去・パスワード強度

| テスト | 結果 |
|---|---|
| クリップボード: ロック時は時間前でも即時消去 | ✅ 成功 |
| クリップボード: 他のアプリが上書きした内容は消去しない | ✅ 成功 |
| クリップボード: 時間経過後、内容が変わっていなければ消去 | ✅ 成功 |
| クリップボード: 更新番号が変わっても内容が同じコードなら消去 | ✅ 成功 |
| パスワード強度の推定(pw: "", expected: Empty) | ✅ 成功 |
| パスワード強度の推定(pw: "Tr0ub4dor&3xK!9z", expected: VeryStrong) | ✅ 成功 |
| パスワード強度の推定(pw: "aaaaaaaaaaaa", expected: Weak) | ✅ 成功 |
| パスワード強度の推定(pw: "abc", expected: VeryWeak) | ✅ 成功 |
| パスワード強度の推定(pw: "correcthorse", expected: Fair) | ✅ 成功 |
| ロック解除の連続失敗で待機時間が段階的に増加（最大60秒） | ✅ 成功 |
| 自動ロック: 無効設定 (0) ではロックしない | ✅ 成功 |
| 自動ロック: 無操作時間が設定値に達したらロック | ✅ 成功 |

### ログへの機密情報出力の防止

| テスト | 結果 |
|---|---|
| ログにシークレット・コード・パスワード・アカウント名を出力しない | ✅ 成功 |
## 2. 画面操作の確認

配布用の `TotpVault.exe` を Windows 互換環境（Wine 11.0, Windows 10 相当）で起動し、キーボード・マウス操作を自動入力して確認しました。

| # | 確認項目 | 結果 |
|---|---|---|
| 1 | 初回起動 → マスターパスワード設定（強度メーター、確認入力の不一致エラー、同意チェック） | ✅ |
| 2 | 再起動 → ロック画面 → 誤ったパスワードで失敗回数表示・待機時間 → 正しいパスワードで解除 | ✅ |
| 3 | シークレットキーの手動登録 → 生成コードが独立計算（Python hmac）と一致 | ✅ |
| 4 | QR コード画像（Google Authenticator 移行形式・3 件）の一括登録、登録済みシークレットの重複スキップ | ✅ |
| 5 | 画面上の QR コードのスキャン → 編集画面に自動入力 → 登録 | ✅ |
| 6 | クリックでコピー（「コピーしました」表示・自動消去までの秒数の通知） | ✅ |
| 7 | 残り 5 秒での赤色表示と次のコードの先行表示 | ✅ |
| 8 | お気に入り登録（上部への固定表示）、検索、該当なし表示、右クリックメニュー | ✅ |
| 9 | 設定画面（自動ロック時間の変更がステータス表示に即時反映） | ✅ |
| 10 | 自動ロック（1 分設定で、最後の操作から 60 秒後にロック） | ✅ |
| 11 | 手動ロック（ロック画面に戻り、コード表示が消去される） | ✅ |
| 12 | 暗号化バックアップの作成（保存先選択 → バックアップ用パスワード設定 → ファイル内容が暗号化されていること） | ✅ |
| 13 | 別のデータフォルダ（別 PC 想定・別マスターパスワード）への復元：誤パスワードの拒否 → 正しいパスワードで 3 件復元、お気に入り状態も復元 | ✅ |

この確認作業で見つかった不具合（ダイアログのスタイル指定の誤り、処理完了後にダイアログが閉じない問題、画面キャプチャ画像のアルファ値による QR 読み取り失敗など）はすべて修正済みです。

> 補足: Windows 10 / 11 の実機での最終確認（SmartScreen の表示、高 DPI・マルチモニター環境での画面スキャンなど）を納品前に実施することを推奨します。
