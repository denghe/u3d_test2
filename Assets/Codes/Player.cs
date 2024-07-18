﻿using System;
using System.Collections.Generic;
using UnityEngine;

public class Player {
    public Scene scene;                                                 // 指向场景
    public Stage stage;                                                 // 指向关卡
    public GO go, mgo;                                                  // 保存底层 u3d 资源. mgo: mini map 用到的那份

    public const float defaultMoveSpeed = (int)(450f / Scene.fps);      // 原始移动速度
    public const float _1_defaultMoveSpeed = 1f / defaultMoveSpeed;
    public const float frameAnimIncrease = _1_defaultMoveSpeed;         // 帧动画前进速度( 针对 defaultMoveSpeed )
    public const float displayScale = 2f;                               // 显示放大修正
    public const float defaultRadius = 13f;                             // 原始半径

    public float frameIndex = 0;                                        // 当前动画帧下标
    public bool flipX;                                                  // 根据移动方向判断要不要反转 x 显示

    public float radius = defaultRadius;                                // 半径. 该数值和玩家体积同步
    public float x, y;                                                  // 坐标( 格子坐标系, 大Y向下 )
    public List<Vector2> positionHistory = new();                       // 历史坐标数组    // todo: 需要自己实现一个 ring buffer 避免 move
    public float radians {                                              // 俯视角度下的角色 前进方向 弧度 ( 可理解为 朝向 )
        get {
            return Mathf.Atan2(scene.playerDirection.y, scene.playerDirection.x);
        }
    }
    public int quitInvincibleTime;                                      // 退出无敌状态的时间点

    public int hp = 100;                                                // 当前血量
    public int maxHp = 100;                                             // 血上限
    public int damage = 10;                                             // 当前基础伤害倍率( 技能上面为实际伤害值 )
    public int defense = 10;                                            // 防御力
    public float criticalRate = 0.05f;                                  // 暴击率
    public float criticalDamageRatio = 1.5f;                            // 暴击伤害倍率
    public float dodgeRate = 0.05f;                                     // 闪避率
    public float moveSpeed = 20;                                        // 当前每帧移动距离
    public int getHurtInvincibleTimeSpan = 6;                           // 受伤短暂无敌时长( 帧 )
    public List<PlayerSkill> skills = new();                            // 玩家技能数组

    public Player(Scene scene_) {
        scene = scene_;

        GO.Pop(ref go);
        go.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        go.r.receiveShadows = true;
        go.Enable();

        GO.Pop(ref mgo, 3);
        mgo.Enable();
        mgo.r.sprite = scene.sprites_player[0];
        mgo.r.material = scene.minimap_material;
        mgo.t.localScale = new Vector3(4, 4, 4);
    }

    public void Init(Stage stage_, float x_, float y_) {
        stage = stage_;
        x = x_;
        y = y_;

        // 预填充一些 positionHistory 数据防越界
        positionHistory.Clear();
        var p = new Vector2(x, y);
        for (int i = 0; i < Scene.fps; i++) {
            positionHistory.Add(p);
        }
    }

    public bool Update() {

        // 玩家控制移动( 条件: 还活着 )
        if (hp > 0 && scene.playerMoving) {
            var mv = scene.playerMoveValue;
            x += mv.x * moveSpeed;
            y += mv.y * moveSpeed;

            // 判断绘制 x 坐标要不要翻转
            if (flipX && mv.x > 0) {
                flipX = false;
            } else if (mv.x < 0) {
                flipX = true;
            }

            // 根据移动速度步进动画帧下表
            frameIndex += frameAnimIncrease * moveSpeed * _1_defaultMoveSpeed;
            var len = scene.sprites_player.Length;
            if (frameIndex >= len) {
                frameIndex -= len;
            }

            // 强行限制移动范围( 理论上讲也可以设计一些临时限制，比如 boss 禁锢 )
            if (x < 0) x = 0;
            else if (x >= Scene.gridWidth) x = Scene.gridWidth - float.Epsilon;
            if (y < 0) y = 0;
            else if (y >= Scene.gridHeight) y = Scene.gridHeight - float.Epsilon;
        }

        // 将坐标写入历史记录( 限定长度 )
        positionHistory.Insert(0, new Vector2(x, y));
        if (positionHistory.Count > Scene.fps) {
            positionHistory.RemoveAt(positionHistory.Count - 1);
        }

        // 驱动技能
        {
            var len = skills.Count;
            for (int i = 0; i < len; ++i) {
                skills[i].Update();
            }
        }

        return false;
    }

    public void Draw() {
        // 同步帧下标
        go.r.sprite = scene.sprites_player[(int)frameIndex];

        // 同步反转状态
        go.r.flipX = flipX;

        // 同步 & 坐标系转换( y 坐标需要反转 )
        var p = new Vector3(x * Scene.designWidthToCameraRatio, 0, -y * Scene.designWidthToCameraRatio);
        go.t.position = p;
        go.t.localScale = new Vector3(displayScale, displayScale, displayScale);

        // 短暂无敌用变白表达
        if (scene.time <= quitInvincibleTime) {
            go.SetColorWhite();
        } else {
            go.SetColorNormal();
        }

        mgo.t.position = p;
    }

    public void DrawGizmos() {
        Gizmos.DrawWireSphere(new Vector3(x * Scene.designWidthToCameraRatio, 0, -y * Scene.designWidthToCameraRatio), radius * Scene.designWidthToCameraRatio);
    }

    public void Destroy() {
        foreach (var skill in skills) {
            skill.Destroy();
        }
#if UNITY_EDITOR
        if (go.g != null)
#endif
        {
            GO.Push(ref go);
        }
#if UNITY_EDITOR
        if (mgo.g != null)
#endif
        {
            GO.Push(ref mgo);
        }
    }

    // 令玩家受伤或死亡
    public void Hurt(int monsterDamage) {
        if (scene.time < quitInvincibleTime) return;    // 无敌中

        var d = (float)monsterDamage;
        d = d * d / (d + defense);
        monsterDamage = (int)d;

        if (hp <= d) {
            hp = 0;      // 玩家死亡
            // todo: 播特效?
        } else {
            hp -= monsterDamage;     // 玩家减血
            quitInvincibleTime = scene.time + getHurtInvincibleTimeSpan;
            // todo: 播特效
        }
    }

}
