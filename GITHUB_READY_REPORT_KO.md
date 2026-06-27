# GitHub 공개용 정리 보고서

## 처리 내용

- 불필요한 중첩 ZIP 파일 제거
- 빌드 산출물, 런타임 데이터, 보관함 DB, 백업 파일이 포함되지 않도록 정리
- `.gitignore`, `.gitattributes`, `.editorconfig` 추가
- `README.md`, `README_KO.md` 작성
- `docs/PROJECT_OVERVIEW_KO.md` 작성
- `docs/TEST_CHECKLIST_KO.md` 작성
- `docs/RELEASE_CHECKLIST_KO.md` 작성
- Visual Studio에서 바로 열 수 있도록 `PrivateGalleryVault.sln` 추가
- UI 문구 중 프로토타입처럼 보이는 표현 일부 정리
- 일부 개발자 주석을 자연스러운 한국어 주석으로 교열
- XAML/XML 구조 검사 완료
- AI 제작을 직접 드러내는 문구, 테스트용 문구, 샘플/더미 문구 검색 완료

## 제외한 항목

- `bin/`, `obj/`, `publish/`, `.vs/`, `.git/`
- `*.db`, `*.sqlite`, `*.log`, `*.pgvbackup`, `settings.json`
- 기존 소스 폴더 안에 들어 있던 중첩 ZIP 파일

## 빌드 확인 안내

이 환경에는 Windows WPF 빌드용 .NET SDK가 없어 실제 `dotnet publish`는 실행하지 못했습니다. 대신 다음 검사를 수행했습니다.

- XAML/XML 파싱 검사
- 공개 전 민감 파일명 패턴 검사
- AI/테스트/샘플성 문구 검색
- GitHub 업로드용 구조 정리

Windows에서 아래 명령으로 최종 빌드를 확인하세요.

```powershell
.\build_release.ps1
```
