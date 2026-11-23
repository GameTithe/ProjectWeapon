# KAN-187 무기 원형과 검증

Unity의 `Assets > Create > ProjectWeapon > Weapon Definition`에서 무기 원형 에셋을 만든다. 원형 ID는 저장이나 참조에 쓰이는 고정 식별자다. 공개한 ID를 바꾸면 같은 이름이라도 다른 원형으로 읽힌다. 표시명과 등급(1~5), 기본 공격과 스킬의 데이터 에셋, 공격 VFX 프리팹을 지정한다. 실물 단위의 ID·위치·소유권·잔고는 개별 시스템이 다룬다. [KAN-187](https://gametithe.atlassian.net/browse/KAN-187) · [KAN-19](https://gametithe.atlassian.net/browse/KAN-19)

에디터 메뉴 `Tools > ProjectWeapon > Validate Weapon Definitions`는 프로젝트의 원형 에셋에서 누락된 필드, 중복 원형 ID와 등급 오류를 확인한다. 실제 무기 에셋과 공격·스킬 효과는 해당 콘텐츠 티켓에서 추가한다.

EditMode에서 등급 경계(1, 5), 등급 오류(0, 6), 중복 ID, 비어 있거나 공백이 붙은 ID, 에셋·이름 및 참조 누락을 검증한다.

```powershell
unity test . --mode EditMode --filter ProjectWeapon.Weapons.Tests --output "$env:TEMP/KAN-187-editmode.xml"
```
