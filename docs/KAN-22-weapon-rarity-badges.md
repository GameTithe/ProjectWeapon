# KAN-22 무기 5등급 픽셀 표식

`Assets/ProjectWeapon/Art/WeaponRarity/`의 16×16px 스프라이트는 32 PPU와 Point 필터를 사용한다. 중앙 표식은 흑백에서도 구분된다. [KAN-22](https://gametithe.atlassian.net/browse/KAN-22) · [KAN-21](https://gametithe.atlassian.net/browse/KAN-21)

| `WeaponDefinition.Grade` | 이름 | 강조색 | 형태 | 참조 파일 |
| --- | --- | --- | --- | --- |
| 1 | 일반 | `#C0C6CE` | 정사각형 | `01-Common.png` |
| 2 | 고급 | `#75D594` | 위를 향한 꺾쇠 | `02-Uncommon.png` |
| 3 | 희귀 | `#7ABFFF` | 빈 마름모 | `03-Rare.png` |
| 4 | 영웅 | `#CF9EFA` | 교차 표식 | `04-Epic.png` |
| 5 | 전설 | `#FFD273` | 왕관 | `05-Legendary.png` |

UI에서 무기 데이터의 정수 등급으로 위 Sprite를 참조한다. 화면에는 16×16px 이상으로 표시하고, 정수 배율과 원래 색상을 유지한다. 이름을 표시하는 화면에서는 등급 이름도 함께 사용한다. 누락된 등급은 화면에서 별도 미지정 상태로 처리한다.

`art-source/Export-WeaponRarity.ps1`이 원본 스프라이트를 재생성한다. `-PreviewFile`을 지정하면 위 행은 원색, 아래 행은 회색조인 검토용 PNG를 출력한다.
