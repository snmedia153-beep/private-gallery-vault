# 릴리즈 체크리스트

1. `bin/`, `obj/`, `publish/` 폴더가 저장소에 포함되지 않았는지 확인합니다.
2. 실제 보관함 데이터, DB, 백업 파일이 포함되지 않았는지 확인합니다.
3. `settings.json`이 포함되지 않았는지 확인합니다.
4. `README.md`의 빌드 방법이 현재 프로젝트 구조와 맞는지 확인합니다.
5. Windows 환경에서 `build_release.ps1`을 실행합니다.
6. `publish/win-x64/PrivateGalleryVault.exe`를 실행하여 기본 흐름을 점검합니다.
7. 새 보관함 생성, 파일 가져오기, 보기, 잠금, 잠금 해제를 확인합니다.
