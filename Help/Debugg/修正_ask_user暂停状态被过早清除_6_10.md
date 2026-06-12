focus ： 修正 ask_user 暂停状态被过早清除导致用户点击无响应的问题
reason ： 用户回答 AI 提问后，RespondToQuestionAsync 在行 515 立即清除 IsAwaitingUserInput = false，导致 UI 提前隐藏问题面板。当 ContinueToolLoopAsync 运行 (~8秒) 时，IsAwaitingUserInput 为 false，用户点击选项按钮找不到暂停状态，被静默忽略。后续 AI 再次 ask_user 时，状态重新设置，但用户已失去交互窗口。
deepreason ： 旧代码在收到用户回答后立即清除 IsAwaitingUserInput 和 PendingAskUserQuestion，然后启动 ContinueToolLoopAsync。在工具循环运行期间，UI 看不到"正在等待"状态，问题面板消失。如果工具循环正常结束（没有新的 ask_user），状态被清除（这是对的）。但如果工具循环中途被另一个 ask_user 中断，状态会在 ContinueToolLoopAsync 返回后才重新设置。在这之间用户点击会被静默忽略。
solution ： 在 ContinueToolLoopAsync 运行期间保持 IsAwaitingUserInput = true（不提前清除）。工具循环结束后，根据 pausedResult 决定：如果有新的 ask_user 暂停，更新新问题并继续显示；如果工具循环正常结束，清除暂停状态。
change : ChatViewModel.cs Line : 514-574
